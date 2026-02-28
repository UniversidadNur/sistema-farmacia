using FarmaciaSalacor.Web.Data;
using FarmaciaSalacor.Web.Models;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FarmaciaSalacor.Web.Areas.Inventarios.Controllers;

[Area("Inventarios")]
public class ProductosController : Controller
{
    private readonly AppDbContext _db;

    public ProductosController(AppDbContext db)
    {
        _db = db;
    }

    [Authorize(Roles = Roles.Admin + "," + Roles.Almacen)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file)
    {
        ViewBag.ActiveModule = "Inventarios";
        ViewBag.ActiveSubModule = "Productos";

        if (file is null || file.Length == 0)
        {
            TempData["ImportMessage"] = "Archivo no válido.";
            return RedirectToAction(nameof(Index));
        }

        using var sr = new StreamReader(file.OpenReadStream());
        var content = await sr.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            TempData["ImportMessage"] = "Archivo vacío.";
            return RedirectToAction(nameof(Index));
        }

        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            TempData["ImportMessage"] = "No se encontraron líneas en el archivo.";
            return RedirectToAction(nameof(Index));
        }

        // detect delimiter
        string header = lines[0];
        char delim = '\t';
        if (header.Contains('|')) delim = '|';
        else if (header.Contains(';')) delim = ';';
        else if (header.Contains(',')) delim = ',';

        // parse header columns if present
        var headers = header.Split(delim).Select(h => h.Trim().ToLowerInvariant()).ToArray();
        int startLine = 0;
        bool hasHeader = headers.Any(h => h.Contains("codigo") || h.Contains("nombre"));
        if (hasHeader) startLine = 1;

        var created = 0;
        var updated = 0;

        for (int i = startLine; i < lines.Length; i++)
        {
            var row = lines[i];
            var parts = row.Split(delim).Select(p => p.Trim()).ToArray();
            string GetField(string name, int idx)
            {
                if (hasHeader)
                {
                    for (int j = 0; j < headers.Length; j++)
                    {
                        if (headers[j].Contains(name)) return j < parts.Length ? parts[j] : string.Empty;
                    }
                    return string.Empty;
                }
                else
                {
                    return idx < parts.Length ? parts[idx] : string.Empty;
                }
            }

            var codigo = GetField("codigo", 0);
            var nombre = GetField("nombre", 1);
            var nombreGen = GetField("generico", 2);
            var forma = GetField("forma", 3);
            var conc = GetField("concentr", 4);
            var categoriaName = GetField("categoria", 5);
            var marcaName = GetField("marca", 6);
            var presentacion = GetField("presentacion", 7);
            var stockTxt = GetField("stock", 8);
            var precioTxt = GetField("precio", 9);
            var vencTxt = GetField("venc", 10);

            if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre)) continue;

            var prod = await _db.Productos.Include(p => p.Categoria).Include(p => p.Marca).FirstOrDefaultAsync(p => p.Codigo == codigo);
            bool isNew = prod is null;
            if (isNew)
            {
                prod = new Producto { Codigo = codigo };
            }

            prod.Nombre = nombre;
            prod.NombreGenerico = string.IsNullOrWhiteSpace(nombreGen) ? null : nombreGen;
            prod.FormaFarmaceutica = string.IsNullOrWhiteSpace(forma) ? null : forma;
            prod.Concentracion = string.IsNullOrWhiteSpace(conc) ? null : conc;
            prod.Presentacion = string.IsNullOrWhiteSpace(presentacion) ? null : presentacion;

            if (decimal.TryParse(stockTxt?.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var stock)) prod.Stock = stock;
            if (decimal.TryParse(precioTxt?.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var precio)) prod.Precio = precio;
            if (DateOnly.TryParse(vencTxt, out var d)) prod.Vencimiento = d;

            // Categoria
            if (!string.IsNullOrWhiteSpace(categoriaName))
            {
                var catName = categoriaName.Trim();
                var cat = await _db.Categorias.FirstOrDefaultAsync(c => c.Nombre.ToLower() == catName.ToLower());
                if (cat is null)
                {
                    cat = new Categoria { Nombre = catName };
                    _db.Categorias.Add(cat);
                    await _db.SaveChangesAsync();
                }
                prod.CategoriaId = cat.Id;
            }

            // Marca
            if (!string.IsNullOrWhiteSpace(marcaName))
            {
                var mName = marcaName.Trim();
                var m = await _db.Marcas.FirstOrDefaultAsync(x => x.Nombre.ToLower() == mName.ToLower());
                if (m is null)
                {
                    m = new Marca { Nombre = mName };
                    _db.Marcas.Add(m);
                    await _db.SaveChangesAsync();
                }
                prod.MarcaId = m.Id;
            }

            if (isNew)
            {
                _db.Productos.Add(prod);
                created++;
            }
            else
            {
                _db.Productos.Update(prod);
                updated++;
            }

            // commit periodically to keep memory small
            if ((created + updated) % 50 == 0)
            {
                await _db.SaveChangesAsync();
            }
        }

        await _db.SaveChangesAsync();

        TempData["ImportMessage"] = $"Importación completada. Nuevos: {created}, Actualizados: {updated}.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.ActiveModule = "Inventarios";
        ViewBag.ActiveSubModule = "Productos";

        var items = await _db.Productos
            .AsNoTracking()
            .Include(x => x.Categoria)
            .Include(x => x.Marca)
            .OrderBy(x => x.Nombre)
            .ToListAsync();

        return View(items);
    }

    [Authorize(Roles = Roles.Admin + "," + Roles.Almacen)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewBag.ActiveModule = "Inventarios";
        ViewBag.ActiveSubModule = "Productos";

        await LoadCatalogosAsync();
        return View(new Producto());
    }

    [Authorize(Roles = Roles.Admin + "," + Roles.Almacen)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Producto model)
    {
        ViewBag.ActiveModule = "Inventarios";
        ViewBag.ActiveSubModule = "Productos";

        if (!ModelState.IsValid)
        {
            await LoadCatalogosAsync();
            return View(model);
        }

        _db.Productos.Add(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Admin + "," + Roles.Almacen)]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        ViewBag.ActiveModule = "Inventarios";
        ViewBag.ActiveSubModule = "Productos";

        var item = await _db.Productos.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        await LoadCatalogosAsync();
        return View(item);
    }

    [Authorize(Roles = Roles.Admin + "," + Roles.Almacen)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Producto model)
    {
        ViewBag.ActiveModule = "Inventarios";
        ViewBag.ActiveSubModule = "Productos";

        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            await LoadCatalogosAsync();
            return View(model);
        }

        _db.Productos.Update(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Admin + "," + Roles.Almacen)]
    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        ViewBag.ActiveModule = "Inventarios";
        ViewBag.ActiveSubModule = "Productos";

        var item = await _db.Productos
            .AsNoTracking()
            .Include(x => x.Categoria)
            .Include(x => x.Marca)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (item is null)
        {
            return NotFound();
        }

        return View(item);
    }

    [Authorize(Roles = Roles.Admin + "," + Roles.Almacen)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var item = await _db.Productos.FindAsync(id);
        if (item is null)
        {
            return RedirectToAction(nameof(Index));
        }

        _db.Productos.Remove(item);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadCatalogosAsync()
    {
        var categorias = await _db.Categorias.AsNoTracking().OrderBy(x => x.Nombre).ToListAsync();
        var marcas = await _db.Marcas.AsNoTracking().OrderBy(x => x.Nombre).ToListAsync();

        ViewBag.Categorias = new SelectList(categorias, "Id", "Nombre");
        ViewBag.Marcas = new SelectList(marcas, "Id", "Nombre");
    }
}
