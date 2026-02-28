using FarmaciaSalacor.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FarmaciaSalacor.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Marca> Marcas => Set<Marca>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<Venta> Ventas => Set<Venta>();
    public DbSet<DetalleVenta> DetallesVenta => Set<DetalleVenta>();
    public DbSet<Compra> Compras => Set<Compra>();
    public DbSet<DetalleCompra> DetallesCompra => Set<DetalleCompra>();
    public DbSet<MovimientoInventario> MovimientosInventario => Set<MovimientoInventario>();
    public DbSet<PagoVenta> PagosVenta => Set<PagoVenta>();
    public DbSet<Lote> Lotes => Set<Lote>();
    public DbSet<Almacen> Almacenes => Set<Almacen>();
    public DbSet<Transferencia> Transferencias => Set<Transferencia>();
    public DbSet<DetalleTransferencia> DetallesTransferencia => Set<DetalleTransferencia>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(50);
            entity.Property(x => x.Rol).HasMaxLength(20);
        });

        modelBuilder.Entity<Categoria>(entity =>
        {
            entity.Property(x => x.Nombre).HasMaxLength(80);
        });

        modelBuilder.Entity<Marca>(entity =>
        {
            entity.Property(x => x.Nombre).HasMaxLength(80);
        });

        modelBuilder.Entity<Producto>(entity =>
        {
            entity.HasIndex(x => x.Codigo).IsUnique();
            entity.Property(x => x.Codigo).HasMaxLength(30);
            entity.Property(x => x.Nombre).HasMaxLength(160);
            entity.Property(x => x.Stock).HasPrecision(18, 2);
            entity.Property(x => x.Precio).HasPrecision(18, 2);
        });

        modelBuilder.Entity<DetalleVenta>(entity =>
        {
            entity.Property(x => x.Cantidad).HasPrecision(18, 2);
            entity.Property(x => x.PrecioUnitario).HasPrecision(18, 2);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<DetalleCompra>(entity =>
        {
            entity.Property(x => x.Cantidad).HasPrecision(18, 2);
            entity.Property(x => x.CostoUnitario).HasPrecision(18, 2);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Venta>(entity =>
        {
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.Pagado).HasPrecision(18, 2);
        });

        modelBuilder.Entity<PagoVenta>(entity =>
        {
            entity.Property(x => x.Monto).HasPrecision(18, 2);
            entity.Property(x => x.Observacion).HasMaxLength(120);
        });

        modelBuilder.Entity<Compra>(entity =>
        {
            entity.Property(x => x.Total).HasPrecision(18, 2);
        });

        modelBuilder.Entity<MovimientoInventario>(entity =>
        {
            entity.Property(x => x.Documento).HasMaxLength(60);
            entity.Property(x => x.Tipo).HasMaxLength(30);
            entity.Property(x => x.Cantidad).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Lote>(entity =>
        {
            entity.Property(x => x.NumeroLote).HasMaxLength(40);
            entity.Property(x => x.Stock).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.ProductoId, x.NumeroLote }).IsUnique();
        });

        modelBuilder.Entity<DetalleTransferencia>(entity =>
        {
            entity.Property(x => x.Cantidad).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Almacen>(entity =>
        {
            entity.Property(x => x.Nombre).HasMaxLength(80);
            entity.HasIndex(x => x.Nombre).IsUnique();
        });

        modelBuilder.Entity<Transferencia>(entity =>
        {
            entity.Property(x => x.Documento).HasMaxLength(30);
            entity.Property(x => x.Observacion).HasMaxLength(200);
        });
    }
}
