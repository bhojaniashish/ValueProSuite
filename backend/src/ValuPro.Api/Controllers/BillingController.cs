using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Billing;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

/// <summary>GST invoices for cases, payments received (with TDS), and the billing dashboard.</summary>
[Route("api/billing")]
[HasPermission(Permissions.Billing.View)]
public sealed class BillingController(IBillingService billing) : ApiControllerBase
{
    /// <summary>This financial year: invoiced, collected, outstanding, overdue, unbilled, by month and by bank.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<BillingSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken ct) => Ok(await billing.SummaryAsync(ct));

    [HttpGet("invoices")]
    [ProducesResponseType<InvoicePage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] InvoiceQuery query, CancellationToken ct) => Ok(await billing.ListAsync(query, ct));

    /// <summary>Submitted cases not billed yet.</summary>
    [HttpGet("unbilled")]
    [ProducesResponseType<IReadOnlyList<UnbilledCase>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Unbilled([FromQuery] Guid? bankId, CancellationToken ct) => Ok(await billing.UnbilledAsync(bankId, ct));

    /// <summary>A draft invoice for the chosen cases (one bank's).</summary>
    [HttpPost("invoices")]
    [HasPermission(Permissions.Billing.Manage)]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(CreateInvoiceRequest request, CancellationToken ct) => From(await billing.CreateAsync(request, ct));

    [HttpGet("invoices/{id:guid}")]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => From(await billing.GetAsync(id, ct));

    [HttpPut("invoices/{id:guid}")]
    [HasPermission(Permissions.Billing.Manage)]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Save(Guid id, SaveInvoiceRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, ct) is { } invalid) return invalid;
        return From(await billing.SaveAsync(id, request, ct));
    }

    [HttpPost("invoices/{id:guid}/issue")]
    [HasPermission(Permissions.Billing.Manage)]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Issue(Guid id, CancellationToken ct) => From(await billing.IssueAsync(id, ct));

    [HttpPost("invoices/{id:guid}/cancel")]
    [HasPermission(Permissions.Billing.Manage)]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id, CancelInvoiceRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, ct) is { } invalid) return invalid;
        return From(await billing.CancelAsync(id, request, ct));
    }

    [HttpDelete("invoices/{id:guid}")]
    [HasPermission(Permissions.Billing.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var r = await billing.DeleteDraftAsync(id, ct);
        return r.IsSuccess ? NoContent() : Problem(r.Error!);
    }

    [HttpPost("invoices/{id:guid}/payments")]
    [HasPermission(Permissions.Billing.Manage)]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPayment(Guid id, AddPaymentRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, ct) is { } invalid) return invalid;
        return From(await billing.AddPaymentAsync(id, request, ct));
    }

    [HttpDelete("invoices/{id:guid}/payments/{paymentId:guid}")]
    [HasPermission(Permissions.Billing.Manage)]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemovePayment(Guid id, Guid paymentId, CancellationToken ct) => From(await billing.RemovePaymentAsync(id, paymentId, ct));

    /// <summary>The tax invoice PDF; shown in the browser unless ?download=true.</summary>
    [HttpGet("invoices/{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, [FromQuery] bool download, CancellationToken ct)
    {
        var r = await billing.PdfAsync(id, ct);
        if (!r.IsSuccess) return Problem(r.Error!);
        return download ? File(r.Value!.Content, "application/pdf", r.Value.FileName) : File(r.Value!.Content, "application/pdf");
    }
}
