using FarmaciaSalacor.Web.Data;
using FarmaciaSalacor.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FarmaciaSalacor.Web.Controllers;

public class ReportesController : Controller
{
    private readonly AppDbContext _db;

    public ReportesController(AppDbContext db)
    {
        _db = db;
    }

    [Authorize]
    public IActionResult Index()
    {
        ViewBag.ActiveModule = "Reportes";
        ViewBag.ActiveSubModule = "Inicio";
        return View();
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> StockBajo(decimal? umbral)
    {
        ViewBag.ActiveModule = "Reportes";
        ViewBag.ActiveSubModule = "StockBajo";

        var th = umbral ?? 0m;
        ViewBag.Umbral = th;

        var items = await _db.Productos
            .AsNoTracking()
            .Where(x => x.Activo && x.Stock <= th)
            .OrderBy(x => x.Stock)
            .ThenBy(x => x.Nombre)
            .ToListAsync();

        return View(items);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Vencimientos(int dias = 30)
    {
        ViewBag.ActiveModule = "Reportes";
        ViewBag.ActiveSubModule = "Vencimientos";

        if (dias < 0) dias = 0;
        if (dias > 365) dias = 365;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var cutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(dias));

        ViewBag.Dias = dias;
        ViewBag.Today = today;
        ViewBag.Cutoff = cutoff;

        var items = await _db.Productos
            .AsNoTracking()
            .Where(x => x.Activo && x.Vencimiento != null && x.Vencimiento <= cutoff)
            .OrderBy(x => x.Vencimiento)
            .ThenBy(x => x.Nombre)
            .ToListAsync();

        return View(items);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Ventas(DateTime? desde, DateTime? hasta)
    {
        ViewBag.ActiveModule = "Reportes";
        ViewBag.ActiveSubModule = "Ventas";

        var query = _db.Ventas
            .AsNoTracking()
            .Include(x => x.Cliente)
            .Include(x => x.Usuario)
            .AsQueryable();

        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue)
        {
            var hastaInclusive = hasta.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(x => x.Fecha <= hastaInclusive);
        }

        var totalDouble = await query.Select(x => (double?)x.Total).SumAsync() ?? 0d;
        ViewBag.Total = Convert.ToDecimal(totalDouble);
        ViewBag.Desde = desde;
        ViewBag.Hasta = hasta;

        var items = await query
            .OrderByDescending(x => x.Fecha)
            .ThenByDescending(x => x.Id)
            .Take(500)
            .ToListAsync();

        return View(items);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Compras(DateTime? desde, DateTime? hasta)
    {
        ViewBag.ActiveModule = "Reportes";
        ViewBag.ActiveSubModule = "Compras";

        var query = _db.Compras
            .AsNoTracking()
            .Include(x => x.Proveedor)
            .Include(x => x.Usuario)
            .AsQueryable();

        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue)
        {
            var hastaInclusive = hasta.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(x => x.Fecha <= hastaInclusive);
        }

        var totalDouble = await query.Select(x => (double?)x.Total).SumAsync() ?? 0d;
        ViewBag.Total = Convert.ToDecimal(totalDouble);
        ViewBag.Desde = desde;
        ViewBag.Hasta = hasta;

        var items = await query
            .OrderByDescending(x => x.Fecha)
            .ThenByDescending(x => x.Id)
            .Take(500)
            .ToListAsync();

        return View(items);
    }
}

