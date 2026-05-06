using KeplerPrint.Models;
using KeplerPrint.Services;
using Microsoft.AspNetCore.Mvc;

namespace KeplerPrint.Controllers
{
    [ApiController]
    [Route("api/print")]
    public class PrintController : ControllerBase
    {
        private readonly PrintService             _print;
        private readonly ILogger<PrintController> _logger;

        public PrintController(PrintService print, ILogger<PrintController> logger)
        {
            _print  = print;
            _logger = logger;
        }

        [HttpGet("health")]
        public IActionResult Health() =>
            Ok(new { ok = true, timestamp = DateTime.Now, proyecto = "Kepler-TrackAlliance" });

        [HttpPost("pilot")]
        [HttpPost("pilot{suffix}")]
        [HttpPost("piloto")]
        public IActionResult Pilot([FromBody] PrintPilotoRequest req)
        {
            try   { _print.ImprimirTicketPiloto(req);   return Ok(new { ok = true }); }
            catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
        }

        [HttpPost("vehicle")]
        [HttpPost("vehicle{suffix}")]
        [HttpPost("vehiculo")]
        public IActionResult Vehicle([FromBody] PrintVehiculoRequest req)
        {
            try   { _print.ImprimirTicketVehiculo(req); return Ok(new { ok = true }); }
            catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
        }

        [HttpPost("turn")]
        [HttpPost("turn{suffix}")]
        [HttpPost("turno")]
        public IActionResult Turn([FromBody] PrintTurnoRequest req)
        {
            try   { _print.ImprimirTicketTurno(req);    return Ok(new { ok = true }); }
            catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
        }

        [HttpPost("completo")]
        public IActionResult Completo([FromBody] PrintRegistroCompletoRequest req)
        {
            try   { _print.ImprimirRegistroCompleto(req); return Ok(new { ok = true }); }
            catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
        }

        [HttpGet("printers")]
        public IActionResult Printers()
        {
            try
            {
                return Ok(new
                {
                    ok = true,
                    printers = _print.GetInstalledPrinters(),
                    printerPorts = _print.GetInstalledPrintersWithPorts()
                });
            }
            catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
        }
    }
}
