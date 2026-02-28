using FarmaciaSalacor.Web.Data;
using FarmaciaSalacor.Web.Models;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
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

    private static string NormalizeHeader(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        // Lowercase + trim + remove diacritics (Acción -> accion)
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

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
        var headers = header.Split(delim).Select(NormalizeHeader).ToArray();
        int startLine = 0;
        bool hasHeader = headers.Any(h => h.Contains("codigo") || h.Contains("cod") || h.Contains("nombre"));
        if (hasHeader) startLine = 1;

        var created = 0;
        var updated = 0;

        for (int i = startLine; i < lines.Length; i++)
        {
            var row = lines[i];
            var parts = row.Split(delim).Select(p => p.Trim()).ToArray();

            // Some exports include an extra empty first column, shifting all values by +1.
            if (hasHeader && parts.Length == headers.Length + 1 && string.IsNullOrWhiteSpace(parts[0]))
            {
                parts = parts.Skip(1).ToArray();
            }

            string GetFieldByHeaderContains(string headerContains, int idx)
            {
                if (hasHeader)
                {
                    var needle = NormalizeHeader(headerContains);
                    for (int j = 0; j < headers.Length; j++)
                    {
                        if (headers[j].Contains(needle)) return j < parts.Length ? parts[j] : string.Empty;
                    }
                    return string.Empty;
                }
                else
                {
                    return idx < parts.Length ? parts[idx] : string.Empty;
                }
            }

            string GetFieldAny(int idx, params string[] headerContainsCandidates)
            {
                if (!hasHeader) return idx < parts.Length ? parts[idx] : string.Empty;

                foreach (var candidate in headerContainsCandidates)
                {
                    var val = GetFieldByHeaderContains(candidate, idx);
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
                return string.Empty;
            }

            var codigo = GetFieldAny(0, "codigo", "cod");
            var nombre = GetFieldAny(1, "nombre comercial", "nombre");
            var nombreGen = GetFieldAny(2, "nombre generico", "generico");
            var forma = GetFieldAny(3, "forma farmaceutica", "forma");
            var conc = GetFieldAny(4, "concentracion", "concentr");

            // En algunos proveedores la categoría viene como “Acción Terapéutica”.
            var categoriaName = GetFieldAny(5, "categoria", "accion terapeutica", "accion");

            // En algunos proveedores la marca viene como “Laboratorio”.
            var marcaName = GetFieldAny(6, "marca", "laboratorio", "lab");

            var presentacion = GetFieldAny(7, "presentacion");
            var stockTxt = GetFieldAny(8, "stock", "existencia");
            var precioTxt = GetFieldAny(9, "precio venta", "precio");
            var vencTxt = GetFieldAny(10, "vencimiento", "venc");

            if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre)) continue;

            var prod = await _db.Productos
                .Include(p => p.Categoria)
                .Include(p => p.Marca)
                .FirstOrDefaultAsync(p => p.Codigo == codigo);

            bool isNew;
            if (prod is null)
            {
                prod = new Producto { Codigo = codigo };
                isNew = true;
            }
            else
            {
                isNew = false;
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
                var catNameLower = catName.ToLower();
                var cat = await _db.Categorias.FirstOrDefaultAsync(c => c.Nombre != null && c.Nombre.ToLower() == catNameLower);
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
                var mNameLower = mName.ToLower();
                var m = await _db.Marcas.FirstOrDefaultAsync(x => x.Nombre != null && x.Nombre.ToLower() == mNameLower);
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
