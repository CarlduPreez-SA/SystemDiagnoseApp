using System.Management;

namespace SystemDiagnoseApp.Services;

/// <summary>Thin helpers around WMI/CIM so checks don't each repeat the ceremony.</summary>
public static class CimService
{
    /// <summary>Query a namespace and return each row as a case-insensitive property bag.</summary>
    public static List<Dictionary<string, object?>> Query(
        string query, string @namespace = @"root\cimv2")
    {
        var rows = new List<Dictionary<string, object?>>();

        var scope = new ManagementScope(@namespace);
        scope.Connect();

        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
        using var results = searcher.Get();

        foreach (ManagementBaseObject item in results)
        {
            using (item)
            {
                var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (PropertyData prop in item.Properties)
                    bag[prop.Name] = prop.Value;
                rows.Add(bag);
            }
        }

        return rows;
    }

    /// <summary>Query that returns an empty list instead of throwing when the class/namespace is missing.</summary>
    public static List<Dictionary<string, object?>> TryQuery(
        string query, string @namespace = @"root\cimv2")
    {
        try { return Query(query, @namespace); }
        catch { return []; }
    }

    public static string? GetString(this Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) && v is not null ? v.ToString() : null;

    public static long? GetLong(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null) return null;
        try { return Convert.ToInt64(v); } catch { return null; }
    }

    public static int? GetInt(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null) return null;
        try { return Convert.ToInt32(v); } catch { return null; }
    }

    public static bool? GetBool(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null) return null;
        try { return Convert.ToBoolean(v); } catch { return null; }
    }

    /// <summary>Convert a WMI CIM_DATETIME string (yyyyMMddHHmmss.ffffff±UUU) to a DateTime.</summary>
    public static DateTime? ToDateTime(string? cimDateTime)
    {
        if (string.IsNullOrWhiteSpace(cimDateTime) || cimDateTime.Length < 14) return null;
        try { return ManagementDateTimeConverter.ToDateTime(cimDateTime); }
        catch { return null; }
    }
}
