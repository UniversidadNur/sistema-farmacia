using System.Diagnostics;
using FarmaciaSalacor.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FarmaciaSalacor.Web.Models;
using FarmaciaSalacor.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FarmaciaSalacor.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AppDbContext _db;

    public HomeController(ILogger<HomeController> logger, AppDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.ActiveModule = "Escritorio";

        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var monthStart = new DateTime(today.Year, today.Month, 1);
        var nextMonth = monthStart.AddMonths(1);

        // SQLite no soporta SUM sobre 'decimal' (EF Core lo traduce a tipo no agregable en SQLite).
        // Agregamos como double en SQL y convertimos a decimal.
        var ventasHoyDouble = await _db.Ventas
            .AsNoTracking()
            .Where(x => x.Fecha >= today && x.Fecha < tomorrow)
            .Select(x => (double?)x.Total)
            .SumAsync() ?? 0d;
        var ventasHoy = Convert.ToDecimal(ventasHoyDouble);

        var ventasHoyCount = await _db.Ventas
            .AsNoTracking()
            .CountAsync(x => x.Fecha >= today && x.Fecha < tomorrow);

        var comprasHoyDouble = await _db.Compras
            .AsNoTracking()
            .Where(x => x.Fecha >= today && x.Fecha < tomorrow)
            .Select(x => (double?)x.Total)
            .SumAsync() ?? 0d;
        var comprasHoy = Convert.ToDecimal(comprasHoyDouble);

        var comprasHoyCount = await _db.Compras
            .AsNoTracking()
            .CountAsync(x => x.Fecha >= today && x.Fecha < tomorrow);

        var ventasMesDouble = await _db.Ventas
            .AsNoTracking()
            .Where(x => x.Fecha >= monthStart && x.Fecha < nextMonth)
            .Select(x => (double?)x.Total)
            .SumAsync() ?? 0d;
        var ventasMes = Convert.ToDecimal(ventasMesDouble);

        var comprasMesDouble = await _db.Compras
            .AsNoTracking()
            .Where(x => x.Fecha >= monthStart && x.Fecha < nextMonth)
            .Select(x => (double?)x.Total)
            .SumAsync() ?? 0d;
        var comprasMes = Convert.ToDecimal(comprasMesDouble);

        var stockTotalDouble = await _db.Productos
            .AsNoTracking()
            .Select(x => (double?)x.Stock)
            .SumAsync() ?? 0d;
        var stockTotal = Convert.ToDecimal(stockTotalDouble);

        var porVencer = await _db.Productos
            .AsNoTracking()
            .CountAsync(x => x.Vencimiento != null && x.Vencimiento <= DateOnly.FromDateTime(today.AddDays(30)));

        var movimientos = await _db.MovimientosInventario
            .AsNoTracking()
            .OrderByDescending(x => x.Fecha)
            .Take(15)
            .ToListAsync();

        var stockBajo = await _db.Productos
            .AsNoTracking()
            .CountAsync(x => x.Stock <= 0);

        var vencidos = await _db.Productos
            .AsNoTracking()
            .CountAsync(x => x.Vencimiento != null && x.Vencimiento < DateOnly.FromDateTime(today));

        // CxC: saldo total de ventas a crédito
        var cxcItems = await _db.Ventas
            .AsNoTracking()
            .Where(x => x.EsCredito)
            .Select(x => new { x.Total, x.Pagado })
            .ToListAsync();

        var cxcSaldo = cxcItems.Sum(x => x.Total - x.Pagado);
        var cxcCount = cxcItems.Count(x => (x.Total - x.Pagado) > 0);

        // Serie últimos 7 días
        var serieStart = today.AddDays(-6);
        var ventas7 = await _db.Ventas
            .AsNoTracking()
            .Where(x => x.Fecha >= serieStart && x.Fecha < tomorrow)
            .Select(x => new { Dia = x.Fecha.Date, x.Total })
            .ToListAsync();
        var compras7 = await _db.Compras
            .AsNoTracking()
            .Where(x => x.Fecha >= serieStart && x.Fecha < tomorrow)
            .Select(x => new { Dia = x.Fecha.Date, x.Total })
            .ToListAsync();

        var serie = new List<SerieDiaViewModel>();
        for (var i = 0; i < 7; i++)
        {
            var d = serieStart.AddDays(i).Date;
            var v = ventas7.Where(x => x.Dia == d).Sum(x => x.Total);
            var c = compras7.Where(x => x.Dia == d).Sum(x => x.Total);
            serie.Add(new SerieDiaViewModel { Dia = d, VentasTotal = v, ComprasTotal = c });
        }

        var vm = new EscritorioViewModel
        {
            Today = today,
            ComprasDiaCount = comprasHoyCount,
            ComprasDiaTotal = comprasHoy,
            VentasDiaCount = ventasHoyCount,
            VentasDiaTotal = ventasHoy,
            ComprasMesTotal = comprasMes,
            VentasMesTotal = ventasMes,
            StockTotal = stockTotal,
            StockBajoCount = stockBajo,
            PorVencerCount = porVencer,
            VencidosCount = vencidos,
            CuentasPorCobrarCount = cxcCount,
            CuentasPorCobrarSaldo = cxcSaldo,
            MovimientosRecientes = movimientos,
            Serie7Dias = serie
        };

        return View(vm);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [AllowAnonymous]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
