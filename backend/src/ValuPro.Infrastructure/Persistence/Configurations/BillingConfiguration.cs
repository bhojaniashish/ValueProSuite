using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Billing;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("Invoices");
        // Issued numbers are unique per firm (drafts have none).
        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[Number] IS NOT NULL");
        b.HasIndex(x => new { x.TenantId, x.Status, x.InvoiceDate });
        b.HasOne<Bank>().WithMany().HasForeignKey(x => x.BankId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Number).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.BillTo).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.BillToName).HasMaxLength(200).IsRequired();
        b.Property(x => x.BillToAddress).HasMaxLength(400);
        b.Property(x => x.BillToGstin).HasMaxLength(15);
        b.Property(x => x.BillToStateCode).HasMaxLength(2);
        foreach (var p in new[] { nameof(Invoice.GstRate) }) b.Property(p).HasPrecision(5, 2);
        foreach (var p in new[] { nameof(Invoice.Taxable), nameof(Invoice.Cgst), nameof(Invoice.Sgst), nameof(Invoice.Igst), nameof(Invoice.RoundOff),
                     nameof(Invoice.Total), nameof(Invoice.Received), nameof(Invoice.Tds) })
            b.Property(p).HasPrecision(14, 2);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.IssuedByName).HasMaxLength(120);
        b.Property(x => x.CancelledByName).HasMaxLength(120);
        b.Property(x => x.CancelReason).HasMaxLength(300);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.Outstanding);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> b)
    {
        b.ToTable("InvoiceLines");
        b.HasIndex(x => x.AssignmentId);
        b.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Description).HasMaxLength(300).IsRequired();
        b.Property(x => x.Sac).HasMaxLength(10);
        b.Property(x => x.Amount).HasPrecision(14, 2);
    }
}

public sealed class InvoicePaymentConfiguration : IEntityTypeConfiguration<InvoicePayment>
{
    public void Configure(EntityTypeBuilder<InvoicePayment> b)
    {
        b.ToTable("InvoicePayments");
        b.Property(x => x.Amount).HasPrecision(14, 2);
        b.Property(x => x.Tds).HasPrecision(14, 2);
        b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.RecordedByName).HasMaxLength(120).IsRequired();
    }
}
