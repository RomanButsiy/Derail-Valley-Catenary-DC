using System;
using System.Collections.Generic;
using System.Text;
namespace CatenaryDC.utilities;

/// <summary>Small CSV reader: quoted fields, escaped quotes, BOM and CR/LF. No game-private Mono.Csv dependency.</summary>
internal static class CsvTable
{
    internal static List<List<string>> Read(string text)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, endedQuote = false, fieldStarted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; endedQuote = true; }
                }
                else field.Append(c);
                continue;
            }
            if (c == '"')
            {
                if (fieldStarted || endedQuote) throw new FormatException("Unexpected quote in CSV field.");
                quoted = fieldStarted = true;
            }
            else if (c == ',' || c == '\r' || c == '\n')
            {
                row.Add(field.ToString()); field.Clear(); fieldStarted = endedQuote = false;
                if (c != ',')
                {
                    if (row.Count > 1 || row[0].Trim().Length > 0) rows.Add(row);
                    row = new List<string>();
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                }
            }
            else
            {
                if (endedQuote)
                {
                    if (!char.IsWhiteSpace(c)) throw new FormatException("Unexpected data after a quoted CSV field.");
                }
                else { field.Append(c); fieldStarted = true; }
            }
        }
        if (quoted) throw new FormatException("Unterminated quoted CSV field.");
        if (fieldStarted || endedQuote || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Trim().Length > 0) rows.Add(row);
        }
        return rows;
    }
}
