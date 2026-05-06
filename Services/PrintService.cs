using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Management;
using ESCPOS_NET.Emitters;
using KeplerPrint.Helpers;
using KeplerPrint.Models;

namespace KeplerPrint.Services
{
    public class PrintService
    {
        private readonly IConfiguration        _config;
        private readonly ILogger<PrintService> _logger;

        public PrintService(IConfiguration config, ILogger<PrintService> logger)
        {
            _config = config;
            _logger = logger;
        }

        private string NombreImpresora =>
            _config["Impresora:Nombre"] ?? string.Empty;

        public IEnumerable<string> GetInstalledPrinters() =>
            PrinterSettings.InstalledPrinters.Cast<string>();

        public IEnumerable<(string Name, string Port)> GetInstalledPrintersWithPorts()
        {
            var printers = new List<(string Name, string Port)>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, PortName FROM Win32_Printer");
                foreach (ManagementObject printer in searcher.Get())
                {
                    printers.Add((printer["Name"]?.ToString() ?? string.Empty,
                                  printer["PortName"]?.ToString() ?? string.Empty));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo obtener el puerto de las impresoras instaladas.");
            }
            return printers;
        }

        private bool PrinterExists(string printerName) =>
            GetInstalledPrinters().Any(p => string.Equals(p, printerName?.Trim(), StringComparison.OrdinalIgnoreCase));

        private string ResolvePrinterName(string configuredPrinter)
        {
            var candidate = configuredPrinter?.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
                return string.Empty;

            if (PrinterExists(candidate))
                return candidate;

            var portMatch = GetInstalledPrintersWithPorts()
                .FirstOrDefault(p => string.Equals(p.Port, candidate, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(portMatch.Name))
                return portMatch.Name;

            var partialMatch = GetInstalledPrinters()
                .FirstOrDefault(p => p.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrWhiteSpace(partialMatch))
                return partialMatch;

            return candidate;
        }

        private static readonly string[] F1Art =
        {
            "       .-----------.          ",
            "     ./             \\.        ",
            "    /                 \\       ",
            "   / ._______________. \\      ",
            "  | |               | |       ",
            "  | |               | |       ",
            "  | |               | |       ",
            "   \\ `_______________' /      ",
            "    \\                 /       ",
            "     `-.___________.`         ",
            "        |_________|           ",
        };

        private byte[] BuildTicket(Action<List<byte[]>> contenido)
        {
            var e      = new EPSON();
            var chunks = new List<byte[]>();
            chunks.Add(e.Initialize());
            chunks.Add(e.CenterAlign());
            foreach (var line in F1Art)
                chunks.Add(e.PrintLine(line));
            contenido(chunks);
            chunks.Add(e.PrintLine("================================"));
            chunks.Add(e.CenterAlign());
            chunks.Add(e.PrintLine("KEPLER-TRACKALLIANCE"));
            chunks.Add(e.PrintLine(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")));
            chunks.Add(e.FeedLines(4));
            chunks.Add(e.FullCut());
            int total  = chunks.Sum(c => c.Length);
            var result = new byte[total];
            int offset = 0;
            foreach (var chunk in chunks)
            { Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length); offset += chunk.Length; }
            return result;
        }

        private void Imprimir(byte[] datos)
        {
            if (string.IsNullOrWhiteSpace(NombreImpresora))
                throw new InvalidOperationException("No se ha configurado el nombre de la impresora en KeplerPrint/appsettings.json.");

            var resolvedPrinter = ResolvePrinterName(NombreImpresora);
            if (string.IsNullOrWhiteSpace(resolvedPrinter) || !PrinterExists(resolvedPrinter))
            {
                var installed = GetInstalledPrinters();
                var installedPorts = GetInstalledPrintersWithPorts()
                    .Select(p => $"{p.Name} ({p.Port})");
                throw new InvalidOperationException(
                    $"La impresora configurada '{NombreImpresora}' no existe. " +
                    $"Impresoras instaladas: {string.Join(", ", installed)}. " +
                    $"Mapeo de impresoras/puertos: {string.Join(", ", installedPorts)}.");
            }

            _logger.LogInformation("Enviando {Bytes} bytes a '{Impresora}'", datos.Length, resolvedPrinter);
            RawPrinterHelper.SendBytesToPrinter(resolvedPrinter, datos);
        }

        public void ImprimirTicketPiloto(PrintPilotoRequest req)
        {
            _logger.LogInformation("Imprimiendo ticket de piloto: {@Request}", req);
            if (string.IsNullOrWhiteSpace(req.Nombre) || string.IsNullOrWhiteSpace(req.DriverId) ||
                string.IsNullOrWhiteSpace(req.Licencia) || string.IsNullOrWhiteSpace(req.Email) ||
                string.IsNullOrWhiteSpace(req.Fecha))
                throw new ArgumentException("Datos incompletos para ticket de piloto");
            try {
                var e = new EPSON();
                var bytes = BuildTicket(chunks => {
                    chunks.Add(e.LeftAlign());
                    chunks.Add(e.SetStyles(PrintStyle.Bold));
                    chunks.Add(e.PrintLine("REGISTRO DE PILOTO"));
                    chunks.Add(e.SetStyles(PrintStyle.None));
                    chunks.Add(e.PrintLine("--------------------------------"));
                    chunks.Add(e.PrintLine($"Nombre  : {req.Nombre}"));
                    chunks.Add(e.PrintLine($"ID      : {req.DriverId}"));
                    chunks.Add(e.PrintLine($"Licencia: {req.Licencia}"));
                    chunks.Add(e.PrintLine($"Email   : {req.Email}"));
                    chunks.Add(e.PrintLine($"Fecha   : {req.Fecha}"));
                });
                Imprimir(bytes);
            } catch (Exception ex) { _logger.LogError(ex, "Error piloto"); throw; }
        }

        public void ImprimirTicketVehiculo(PrintVehiculoRequest req)
        {
            _logger.LogInformation("Imprimiendo ticket de vehiculo: {@Request}", req);
            if (string.IsNullOrWhiteSpace(req.Modelo) || string.IsNullOrWhiteSpace(req.Categoria) ||
                string.IsNullOrWhiteSpace(req.Vin) || string.IsNullOrWhiteSpace(req.Garage) ||
                string.IsNullOrWhiteSpace(req.Fecha))
                throw new ArgumentException("Datos incompletos para ticket de vehiculo");
            try {
                var e = new EPSON();
                var bytes = BuildTicket(chunks => {
                    chunks.Add(e.LeftAlign());
                    chunks.Add(e.SetStyles(PrintStyle.Bold));
                    chunks.Add(e.PrintLine("REGISTRO DE VEHICULO"));
                    chunks.Add(e.SetStyles(PrintStyle.None));
                    chunks.Add(e.PrintLine("--------------------------------"));
                    chunks.Add(e.PrintLine($"Modelo  : {req.Modelo}"));
                    chunks.Add(e.PrintLine($"Categ.  : {req.Categoria}"));
                    chunks.Add(e.PrintLine($"VIN     : {req.Vin}"));
                    chunks.Add(e.PrintLine($"Garage  : {req.Garage}"));
                    chunks.Add(e.PrintLine($"Fecha   : {req.Fecha}"));
                });
                Imprimir(bytes);
            } catch (Exception ex) { _logger.LogError(ex, "Error vehiculo"); throw; }
        }

        public void ImprimirTicketTurno(PrintTurnoRequest req)
        {
            _logger.LogInformation("Imprimiendo ticket de turno: {@Request}", req);
            if (string.IsNullOrWhiteSpace(req.Turno) || string.IsNullOrWhiteSpace(req.Nombre) ||
                req.Duracion <= 0 || string.IsNullOrWhiteSpace(req.CreatedAt) || string.IsNullOrWhiteSpace(req.Fecha))
                throw new ArgumentException("Datos incompletos para ticket de turno");
            try {
                var e = new EPSON();
                var bytes = BuildTicket(chunks => {
                    chunks.Add(e.CenterAlign());
                    chunks.Add(e.SetStyles(PrintStyle.Bold | PrintStyle.DoubleHeight));
                    chunks.Add(e.PrintLine($"TURNO: {req.Turno}"));
                    chunks.Add(e.SetStyles(PrintStyle.None));
                    chunks.Add(e.PrintLine("--------------------------------"));
                    chunks.Add(e.LeftAlign());
                    chunks.Add(e.PrintLine($"Piloto  : {req.Nombre}"));
                    chunks.Add(e.PrintLine($"Vehiculo: {req.Vehiculo}"));
                    chunks.Add(e.PrintLine($"Duracion: {req.Duracion} min"));
                    chunks.Add(e.PrintLine($"Emitido : {req.CreatedAt}"));
                    chunks.Add(e.CenterAlign());
                    chunks.Add(e.SetStyles(PrintStyle.Bold));
                    chunks.Add(e.PrintLine("*** PENDIENTE ***"));
                    chunks.Add(e.SetStyles(PrintStyle.None));
                });
                Imprimir(bytes);
            } catch (Exception ex) { _logger.LogError(ex, "Error turno"); throw; }
        }

        public void ImprimirRegistroCompleto(PrintRegistroCompletoRequest req)
        {
            _logger.LogInformation("Imprimiendo registro completo: {@Request}", req);
            if (string.IsNullOrWhiteSpace(req.Nombre) || string.IsNullOrWhiteSpace(req.DriverId) ||
                string.IsNullOrWhiteSpace(req.Licencia) || string.IsNullOrWhiteSpace(req.Email) ||
                string.IsNullOrWhiteSpace(req.Modelo) || string.IsNullOrWhiteSpace(req.Categoria) ||
                string.IsNullOrWhiteSpace(req.Vin) || string.IsNullOrWhiteSpace(req.Garage) ||
                string.IsNullOrWhiteSpace(req.Turno) || req.Duracion <= 0 ||
                string.IsNullOrWhiteSpace(req.Fecha))
                throw new ArgumentException("Datos incompletos para registro completo");
            try {
                var e = new EPSON();
                var bytes = BuildTicket(chunks => {
                    chunks.Add(e.CenterAlign());
                    chunks.Add(e.SetStyles(PrintStyle.Bold | PrintStyle.DoubleHeight));
                    chunks.Add(e.PrintLine($"TURNO: {req.Turno}"));
                    chunks.Add(e.SetStyles(PrintStyle.None));
                    chunks.Add(e.PrintLine("================================"));
                    chunks.Add(e.LeftAlign());
                    chunks.Add(e.SetStyles(PrintStyle.Bold));
                    chunks.Add(e.PrintLine("-- PILOTO --"));
                    chunks.Add(e.SetStyles(PrintStyle.None));
                    chunks.Add(e.PrintLine($"Nombre  : {req.Nombre}"));
                    chunks.Add(e.PrintLine($"ID      : {req.DriverId}"));
                    chunks.Add(e.PrintLine($"Licencia: {req.Licencia}"));
                    chunks.Add(e.PrintLine($"Email   : {req.Email}"));
                    chunks.Add(e.PrintLine("--------------------------------"));
                    chunks.Add(e.SetStyles(PrintStyle.Bold));
                    chunks.Add(e.PrintLine("-- VEHICULO --"));
                    chunks.Add(e.SetStyles(PrintStyle.None));
                    chunks.Add(e.PrintLine($"Modelo  : {req.Modelo}"));
                    chunks.Add(e.PrintLine($"Categ.  : {req.Categoria}"));
                    chunks.Add(e.PrintLine($"VIN     : {req.Vin}"));
                    chunks.Add(e.PrintLine($"Garage  : {req.Garage}"));
                    chunks.Add(e.PrintLine("--------------------------------"));
                    chunks.Add(e.CenterAlign());
                    chunks.Add(e.PrintLine($"Duracion: {req.Duracion} min"));
                    chunks.Add(e.SetStyles(PrintStyle.Bold));
                    chunks.Add(e.PrintLine("*** PENDIENTE ***"));
                    chunks.Add(e.SetStyles(PrintStyle.None));
                });
                Imprimir(bytes);
            } catch (Exception ex) { _logger.LogError(ex, "Error registro completo"); throw; }
        }
    }
}
