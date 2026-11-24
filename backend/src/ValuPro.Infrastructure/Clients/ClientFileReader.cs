using System.Text;
using ValuPro.Application.Clients;
using ValuPro.Domain.Clients;

namespace ValuPro.Infrastructure.Clients;

/// <summary>
/// Reads clients from a CSV or Excel (.xlsx) file. The first row holds column names, matched loosely
/// ("GST Number", "gstin" and "GST" all work), so files exported from other tools usually import as-is.
/// </summary>
public static class ClientFileReader
{
    public const int MaxRows = ValuPro.Infrastructure.Import.TabularFile.MaxRows;

    public static readonly string[] Columns = ["Type", "Name", "Mobile", "Email", "GSTIN", "PAN", "Address", "City", "State", "Pincode", "Remarks"];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["type"] = "Type", ["clienttype"] = "Type", ["category"] = "Type",
        ["name"] = "Name", ["clientname"] = "Name", ["fullname"] = "Name",
        ["mobile"] = "Mobile", ["phone"] = "Mobile", ["mobileno"] = "Mobile", ["contact"] = "Mobile", ["contactno"] = "Mobile",
        ["email"] = "Email", ["emailid"] = "Email", ["mail"] = "Email",
        ["gstin"] = "GSTIN", ["gst"] = "GSTIN", ["gstnumber"] = "GSTIN", ["gstno"] = "GSTIN",
        ["pan"] = "PAN", ["pannumber"] = "PAN", ["panno"] = "PAN",
        ["address"] = "Address",
        ["city"] = "City",
        ["state"] = "State",
        ["pincode"] = "Pincode", ["pin"] = "Pincode", ["postalcode"] = "Pincode", ["zip"] = "Pincode",
        ["remarks"] = "Remarks", ["notes"] = "Remarks", ["remark"] = "Remarks",
    };

    /// <returns>Rows keyed by canonical column name, with their 1-based row number in the file; or an error.</returns>
    public static (List<(int Row, Dictionary<string, string> Values)> Rows, string? Error) Read(Stream file, string fileName)
    {
        List<List<string>> table;
        try
        {
            table = ValuPro.Infrastructure.Import.TabularFile.ReadTable(file, fileName);
        }
        catch (FormatException e) { return ([], e.Message); }
        catch (Exception) { return ([], "The file could not be read. Save it as .xlsx or .csv and try again."); }

        if (table.Count == 0) return ([], "The file is empty.");

        var header = table[0].Select(h => Aliases.GetValueOrDefault(Normalize(h))).ToList();
        if (!header.Contains("Name") || !header.Contains("Mobile"))
            return ([], "The first row must contain column names, including Name and Mobile. Download the template to see the layout.");

        var rows = new List<(int, Dictionary<string, string>)>();
        for (var i = 1; i < table.Count; i++)
        {
            var cells = table[i];
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            if (rows.Count == MaxRows) return ([], $"The file has more than {MaxRows} clients. Split it into smaller files.");

            var values = new Dictionary<string, string>();
            for (var c = 0; c < header.Count && c < cells.Count; c++)
                if (header[c] is { } column && !string.IsNullOrWhiteSpace(cells[c])) values[column] = cells[c].Trim();
            rows.Add((i + 1, values));
        }
        return (rows, null);
    }

    public static (SaveClientRequest? Client, string? TypeError) ToRequest(Dictionary<string, string> v)
    {
        ClientType type = ClientType.Individual;
        string? typeError = null;
        if (v.TryGetValue("Type", out var t) && !TryParseType(t, out type))
        {
            typeError = $"Unknown type \"{t}\". Use Individual, Builder, Bank Branch or Advocate.";
            type = ClientType.Individual; // keep the row well-formed; the error still blocks it
        }

        return (new SaveClientRequest(type, v.GetValueOrDefault("Name") ?? "", v.GetValueOrDefault("Mobile") ?? "",
            v.GetValueOrDefault("Email"), v.GetValueOrDefault("GSTIN"), v.GetValueOrDefault("PAN"), v.GetValueOrDefault("Address"),
            v.GetValueOrDefault("City"), v.GetValueOrDefault("State"), v.GetValueOrDefault("Pincode"), v.GetValueOrDefault("Remarks")), typeError);
    }

    public static bool TryParseType(string value, out ClientType type)
    {
        type = Normalize(value) switch
        {
            "individual" or "person" or "applicant" => ClientType.Individual,
            "builder" or "developer" => ClientType.Builder,
            "bankbranch" or "bank" or "branch" => ClientType.BankBranch,
            "advocate" or "lawyer" => ClientType.Advocate,
            _ => (ClientType)(-1),
        };
        return Enum.IsDefined(type);
    }

    public static string Template()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Columns));
        sb.AppendLine("Individual,Suresh Agarwal,98250 41201,suresh@example.com,,AGRPS4521K,\"12, Satellite Road\",Ahmedabad,Gujarat,380015,Home loan applicant");
        sb.AppendLine("Builder,Shree Balaji Developers,90991 23456,info@example.com,24AABCS1429N1Z5,AABCS1429N,\"B-301, Adajan\",Surat,Gujarat,395007,");
        return sb.ToString();
    }

    private static string Normalize(string s) => ValuPro.Infrastructure.Import.TabularFile.Normalize(s);

}
