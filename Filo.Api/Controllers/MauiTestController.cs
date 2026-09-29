using Microsoft.AspNetCore.Mvc;

namespace Filo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MauiTestController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new
            {
                status = "success",
                message = "Filo.Maui successfully connected to Filo.Api"
            });
        }
    }
}