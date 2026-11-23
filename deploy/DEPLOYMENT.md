# Deploying Filo to AWS

This guide deploys the Filo API, the media-processing worker and PostgreSQL to
AWS for a small group of external testers. Nothing here has been deployed yet;
the steps that need real AWS resources are marked **(needs AWS)**.

---

## 1. Recommended architecture

```
 Testers' Filo app ──HTTPS──▶ EC2 instance (Docker Compose)
                                ├─ caddy   : HTTPS (Let's Encrypt), reverse proxy
                                ├─ api     : ASP.NET Core (Filo.Api), port 8080 internal
                                └─ worker  : Python + ffmpeg (Filo.Processing)
                                     │
                     private ────────┼──────────────▶ RDS PostgreSQL (not public)
                                     │
 Testers' Filo app ◀── presigned URLs ──▶ S3 bucket (private)
```

- **One EC2 instance** (`t4g.small`, 2 vCPU / 2 GB, Amazon Linux 2023, arm64)
  runs the API, the worker and Caddy using `deploy/docker-compose.yml`.
- **Amazon RDS for PostgreSQL** (`db.t4g.micro`, single-AZ, 20 GB gp3) with
  automated daily backups. It is only reachable from the EC2 instance.
- **The existing S3 setup** stays as it is. The API signs URLs so media streams
  straight from S3 to the app and never passes through the server.
- **An IAM instance role** gives the API access to S3, so no AWS access keys
  are stored anywhere.

### Why this option

| Option | Monthly cost (approx.) | Fit for Filo |
|---|---|---|
| **EC2 + Docker Compose + RDS** (recommended) | ~$32 | Simple, cheap and predictable. No request time limit, so large uploads (the API accepts up to 6 GB) work. The worker runs on the same machine. RDS takes care of backups. |
| EC2 only, PostgreSQL in a container (budget) | ~$19 | Cheapest. Backups are your job (EBS snapshots), and the database shares 2 GB of RAM with ffmpeg. Enable with `--profile local-db`. |
| Lightsail instance + Lightsail managed DB | ~$27 | Flat pricing and a simple console. Lightsail has no IAM instance roles, so S3 access keys must be stored on the server. |
| App Runner + RDS | ~$40+ | Managed HTTPS and no server to look after. But requests are limited to 120 seconds, which breaks large uploads, and the worker would still need separate hosting. |
| ECS Fargate or Elastic Beanstalk with a load balancer | ~$45+ | The load balancer alone costs about $16/month. Built for scale this project doesn't need. |

**The worker does not need separate hosting.** It polls the API for jobs,
moves media in and out of S3 through presigned URLs (so it has no AWS
credentials), and runs ffmpeg locally. Running it next to the API is the
simplest setup.

### Decisions you need to make

1. **Domain name (required for HTTPS).** Caddy needs a host name, such as
   `api.yourdomain.com`, whose DNS points at the server. Options:
   - a cheap domain from Route 53 (about $14/year plus $0.50/month for the hosted zone) or any registrar;
   - a free dynamic-DNS name.
2. **Database:** RDS (recommended) or PostgreSQL on the instance (the budget
   option above).
3. **S3 bucket:** a new production bucket (recommended, so test data doesn't
   mix with testers' media), or the existing `filo-media-dev-test`.
4. **Release build blocker:** see Section 9.

---

## 2. Costs (approximate, us-east-2, on-demand)

Check every figure in the [AWS Pricing Calculator](https://calculator.aws/). If
your account still has Free Tier allowances or credits, some of these may be
covered.

| Item | Approx. / month |
|---|---|
| EC2 `t4g.small` running 24/7 | $12.30 |
| EBS gp3 30 GB (OS, Docker images, upload temp space) | $2.40 |
| Public IPv4 address (Elastic IP) | $3.65 |
| RDS `db.t4g.micro` single-AZ | $11.70 |
| RDS storage 20 GB gp3 (backups up to the DB size are free) | $2.30 |
| S3 storage | $0.023 per GB |
| **Data transfer out to the internet** | first 100 GB/month free, then about $0.09 per GB |
| Route 53 hosted zone (optional) | $0.50 |
| **Typical total** | **about $32 + S3 storage + streaming traffic** |

**Main cost drivers:**
- **Streaming media to testers** (S3 data transfer out).
- **The instance running around the clock.** You can stop the EC2 instance
  and the RDS database when nobody is testing. RDS restarts automatically
  after 7 days stopped.
- **CPU credits.** Long ffmpeg conversions on a burstable `t4g` instance can
  use up its CPU credits. In the default "unlimited" mode, sustained use is
  billed extra.

**Set a budget alert** (Billing → Budgets, e.g. $40/month) before you create
anything.

---

## 3. Configuration and secrets

The API reads configuration from environment variables. A double underscore
`__` stands for a section separator, e.g. `AWS__BucketName` is `AWS:BucketName`.
On the server everything is set in `deploy/.env`, created from
`deploy/.env.example`. `.env` is git-ignored; never commit it.

| Variable in `.env` | Used as | Notes |
|---|---|---|
| `FILO_DOMAIN` | Caddy host name | Its DNS A record must point at the Elastic IP |
| `FILO_DB_CONNECTION` | `ConnectionStrings__DefaultConnection` | Keep `SSL Mode=Require` |
| `AWS_REGION` | `AWS__Region` | Region of the bucket |
| `FILO_S3_BUCKET` | `AWS__BucketName` (also used for `S3:BucketName`) | **Required.** The dev bucket is no longer a default. |
| `FILO_WORKER_KEY` | `ProcessingWorker__ApiKey` and the worker's `FILO_WORKER_KEY` | Generate with `openssl rand -base64 48` |
| `FILO_VERSION` | Docker image tag | Change it on every deployment; used for rollback |

Set automatically by the Compose file:
- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, so HTTPS is detected behind Caddy
- `DataProtection__KeysPath=/keys`, on a persistent volume, so sign-ins survive
  redeploys

What the API does in Production (implemented and tested locally):
- **Refuses to start** without a connection string or bucket name, with a
  clear message.
- **Hides the diagnostic endpoints** `api/S3Test`, `api/ProcessingTest`,
  `WeatherForecast`, `api/MauiTest` and the OpenAPI document (they return 404).
  `api/S3Test` would otherwise list every object in the bucket without a login.
- **Adds HSTS** to HTTPS responses.

For local development nothing changes: user-secrets and
`appsettings.Development.json` still apply.

---

## 4. AWS setup **(needs AWS)**

Do this once, in the AWS console or with the AWS CLI. These steps create
billable resources.

### 4.1 Network and security groups
Use the default VPC. Create two security groups:
- **`filo-web`** (EC2): inbound TCP 80 and 443 from `0.0.0.0/0`, and TCP 22 from
  **your IP only**. Better still, skip SSH and use Systems Manager Session
  Manager.
- **`filo-db`** (RDS): inbound TCP 5432 **only from the `filo-web` security
  group**.

### 4.2 S3
1. Create the production bucket, or reuse the existing one. Leave **Block all
   public access ON**; presigned URLs work with private buckets.
2. Default encryption: SSE-S3.
3. Optional: a lifecycle rule to abort incomplete multipart uploads after 7 days.
4. No CORS rule is needed; the client is a native app, not a browser.

### 4.3 IAM role for the instance
1. Create a policy from `deploy/iam-s3-policy.json`, replacing
   `REPLACE_WITH_BUCKET_NAME`. It allows only `GetObject` and `PutObject`
   under `users/*`, plus `ListBucket` for that prefix.
2. Create a role for **EC2** with that policy, plus `AmazonSSMManagedInstanceCore`
   if you'll use Session Manager. Name it `filo-ec2-role`.

> Without working AWS credentials, **every** API endpoint whose controller uses
> S3 fails with a 500, including playlists and processing jobs, not just
> uploads. Make sure the role is attached.

### 4.4 RDS PostgreSQL
- Engine PostgreSQL 17, template **Free tier / Dev-Test**, `db.t4g.micro`,
  single-AZ, 20 GB gp3.
- Master user `filo_admin`, password stored somewhere safe. Initial database
  name `filo`.
- **Public access: No.** Security group `filo-db`.
- Automated backups: 7 days.

### 4.5 EC2 instance
- Amazon Linux 2023 **arm64**, `t4g.small`, 30 GB gp3, security group
  `filo-web`, IAM role `filo-ec2-role`.
- Metadata options: **IMDSv2 required, hop limit 2.** Containers need hop
  limit 2 to reach the instance role's credentials.
- Allocate an **Elastic IP**, associate it, and point your domain's **A
  record** at it.

---

## 5. Prepare the database

### 5.1 Create the migration script (local machine)
```powershell
dotnet ef migrations script --idempotent --project Filo.Api -o deploy\out\migrate.sql
```
The script is **idempotent**: each migration is applied only if it is missing,
so running it again is safe. This was verified locally: it applied all 7
migrations to an empty PostgreSQL database, and a second run made no changes.
`deploy/out/` contains generated files; don't commit it.

### 5.2 Apply it and create the app user **(needs AWS)**
From the EC2 instance (RDS is not public), after Docker is installed
(Section 6.1). Copy `migrate.sql` to the server first, e.g. with `scp`.
```bash
export RDS=filo-db.xxxxxxxx.us-east-2.rds.amazonaws.com
read -s -p "filo_admin password: " PGPASSWORD; export PGPASSWORD; echo

# Apply migrations as the owner
docker run --rm -i -e PGPASSWORD postgres:17 \
  psql "host=$RDS dbname=filo user=filo_admin sslmode=require" -v ON_ERROR_STOP=1 -f - < migrate.sql
```
Then create a least-privilege user for the API. Run `psql` interactively with
the same `docker run` command, but without `-f -`:
```sql
CREATE ROLE filo_app LOGIN PASSWORD 'a-long-random-password';
GRANT CONNECT ON DATABASE filo TO filo_app;
GRANT USAGE ON SCHEMA public TO filo_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO filo_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO filo_app;
ALTER DEFAULT PRIVILEGES FOR ROLE filo_admin IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO filo_app;
ALTER DEFAULT PRIVILEGES FOR ROLE filo_admin IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO filo_app;
```
Use `filo_app` in `FILO_DB_CONNECTION`. Migrations always run as `filo_admin`.
The API never applies migrations by itself.

---

## 6. Deploy the API and worker **(needs AWS)**

### 6.1 One-time server setup
```bash
sudo dnf install -y docker git
sudo systemctl enable --now docker
sudo usermod -aG docker ec2-user          # log out and back in afterwards

# Docker Compose plugin (arm64)
sudo mkdir -p /usr/local/lib/docker/cli-plugins
sudo curl -SL https://github.com/docker/compose/releases/latest/download/docker-compose-linux-aarch64 \
  -o /usr/local/lib/docker/cli-plugins/docker-compose
sudo chmod +x /usr/local/lib/docker/cli-plugins/docker-compose

# 2 GB swap: building the .NET image on a 2 GB instance needs it
sudo dd if=/dev/zero of=/swapfile bs=1M count=2048 && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile swap swap defaults 0 0' | sudo tee -a /etc/fstab
```

### 6.2 Get the code and configure
```bash
git clone https://github.com/The-Legendary-Seabear/FiloMediaPlayer.git filo   # private repo: use a read-only deploy key or token
cd filo/deploy
cp .env.example .env && chmod 600 .env
nano .env                                  # fill in real values
```

### 6.3 Start
```bash
docker compose up -d --build
docker compose ps
docker compose logs -f api worker caddy    # Ctrl+C to stop following
```
Caddy fetches the HTTPS certificate on first start. Ports 80 and 443 must be
reachable and DNS must already point at the server.

Logs go to `docker compose logs` and are rotated (5 × 10 MB per service).
Shipping them to CloudWatch is optional and not needed for testing.

---

## 7. Verify the deployment

```bash
curl -i https://api.yourdomain.com/health          # 200 Healthy: the API is running
curl -i https://api.yourdomain.com/health/ready    # 200 Healthy: the API can reach the database
curl -i https://api.yourdomain.com/api/S3Test      # 404: diagnostics hidden
curl -i https://api.yourdomain.com/api/playlists   # 401: login required
docker compose logs worker | tail                  # worker polling, no 401 errors
```
Then, from a client built for this server (Section 8):
1. Register, log in, and create a playlist.
2. Upload a short audio file and a thumbnail. Play the file and seek in it.
3. Queue a conversion or grafting job and watch the worker logs.
4. Restart the API (`docker compose restart api`). You should still be signed
   in, because the Data Protection keys are kept on a volume.

`/health/ready` returning 503 means the database can't be reached. Check the
`filo-db` security group, the connection string, and `SSL Mode`.

---

## 8. Connect the MAUI app

The API address is compiled into the app. Normal builds still use
`https://localhost:7038/`. To build for testers:
```powershell
dotnet publish FiloMediaPlayer\FiloMediaPlayer.csproj -c Release `
  -f net10.0-windows10.0.26100.0 -p:Platform=x64 `
  -p:FiloApiBaseUrl=https://api.yourdomain.com/ `
  -o publish\filo-windows
```
- Only `https://` addresses are accepted (plain `http` is allowed only for
  `localhost`), so passwords and tokens are never sent unencrypted.
- Zip `publish\filo-windows` and send it to testers; they run
  `FiloMediaPlayer.exe`.
- The publish step also copies `WinRT.Host.runtimeconfig.json`, which the
  audio-normalization effect needs. Without it normalization is silently
  disabled; playback still works.
- **Test the zip on a PC that has never had the development tools installed.**
  If it complains about a missing **Windows App Runtime**, either have testers
  install it or publish with `-p:WindowsAppSDKSelfContained=true` (larger, but
  nothing to install).

---

## 9. Known blockers and limitations

- **Release builds of the client currently fail.** Three unused controls left
  over from the MAUI project template (`Pages/Controls/CategoryChart.xaml`,
  `ProjectCardView.xaml`, `TaskView.xaml`) reference a `PageModels` namespace
  that no longer exists. Debug builds don't compile XAML strictly, which is
  why this hasn't shown up before. **These files must be removed or excluded
  before Section 8 will work.** With them excluded, a Release publish was
  tested and succeeds.
- Presigned media links last up to 2 hours. When signed with instance-role
  credentials, they can expire sooner if those credentials rotate first.
  Reopening the track gets a fresh link.
- Uploads go through the API to S3, so the instance needs free disk space
  about the size of the largest upload. Uploading directly to S3 with
  presigned PUT URLs would remove this, but needs API and client changes.
- A single instance means a short outage on each deploy (a few seconds) and
  during instance maintenance. That's fine for testing.

---

## 10. Update and roll back

### Update
```bash
cd ~/filo && git pull
cd deploy
# 1. If there are new migrations: take an RDS snapshot first, then apply the
#    new idempotent script (Section 5.2). Keep migrations backward-compatible
#    so the old API still works if you roll back.
# 2. Bump FILO_VERSION in .env (e.g. 1.0.0 -> 1.0.1), then:
docker compose up -d --build
curl -fsS https://api.yourdomain.com/health/ready
```

### Roll back
Previous images stay on the server under their old tags.
```bash
# Set FILO_VERSION back to the previous value in .env, then:
docker compose up -d --no-build
```
Database: restore the snapshot taken before the migration (RDS → Snapshots →
Restore), or use point-in-time restore. Either creates a **new** instance;
point `FILO_DB_CONNECTION` at it and restart the API.

Clean up old images now and then with `docker image prune`.

---

## 11. What was verified locally, and what needs AWS

**Verified locally**, against a throwaway PostgreSQL database (since dropped)
and the published Release API running outside Visual Studio in Production
mode. 29 of 29 checks passed:
- the idempotent migration script: a fresh apply, then a safe re-run;
- health and readiness endpoints, including readiness reporting a missing
  database without leaking details;
- diagnostic endpoints and the OpenAPI document hidden in Production;
- worker key enforcement;
- register, login, `/me`, and creating and listing playlists;
- HSTS sent behind the proxy;
- tokens still valid after an API restart;
- a clear failure when required configuration is missing.

Development mode was also re-checked and is unchanged. The client builds with
the default address, and the address override works.

**Not verified (needs Docker or AWS):**
- building and running the Docker images (Docker isn't installed on the
  development PC);
- Caddy certificate issuance;
- RDS connectivity over SSL;
- instance-role access to S3;
- the worker inside its container;
- real uploads and streaming over the internet;
- the published client on a clean PC.
