using FarmaciaSalacor.Web.Data;
using FarmaciaSalacor.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

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

        // En SQLite, 'decimal' se guarda como TEXT y puede haber valores con coma decimal ("1,5") u otros
        // formatos que rompen la materialización a decimal. Usamos SQL seguro con CAST/REPLACE.
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var list = new List<Producto>();

            await using var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT Id,
       Codigo,
       Nombre,
       CAST(REPLACE(Stock, ',', '.') AS REAL) AS StockNum
FROM Productos
WHERE Activo = 1
  AND CAST(REPLACE(Stock, ',', '.') AS REAL) <= @th
ORDER BY CAST(REPLACE(Stock, ',', '.') AS REAL), Nombre;";

            var p = cmd.CreateParameter();
            p.ParameterName = "@th";
            p.Value = (double)th;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var codigo = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var nombre = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var stockNum = reader.IsDBNull(3) ? 0d : reader.GetDouble(3);

                list.Add(new Producto
                {
                    Id = id,
                    Codigo = codigo,
                    Nombre = nombre,
                    Stock = Convert.ToDecimal(stockNum)
                });
            }

            return View(list);
        }

        var items = await _db.Productos
            .AsNoTracking()
            .Where(x => x.Activo && x.Stock <= th)
            .OrderBy(x => x.Stock)
            .ThenBy(x => x.Nombre)
            .Select(x => new Producto
            {
                Id = x.Id,
                Codigo = x.Codigo,
                Nombre = x.Nombre,
                Stock = x.Stock
            })
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

