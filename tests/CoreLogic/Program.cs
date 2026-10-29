// Links the actual production math/state/CSV sources, not a Python reimplementation.
// No game DLLs or Unity required. This test runner must be executed locally with dotnet.
using System;
using System.Collections.Generic;
using CatenaryDC.Power;
using CatenaryDC.utilities;

internal static class Program
{
    private static int _passed, _failed;
    private static void Check(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Close(float actual, float expected, float tolerance = 0.001f) => Assert(Math.Abs(actual - expected) < tolerance, actual + " != " + expected);
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static SubstationState Trip()
    {
        var s = new SubstationState(1650, 100, false);
        bool tripped = false;
        for (int i = 0; i < 6; i++) { s.AddLoad(200); tripped |= s.Tick(1, 0); }
        Assert(tripped && s.IsTripped); Close(s.Voltage, 0); return s;
    }
    private static int Main()
    {
        Check("distance: Z contributes quadratically", () => Close(ElectricalMath.Distance(0, 0, 0, 0, 1000000, 0), 1000));
        Check("distance: negative Z symmetry", () => Close(ElectricalMath.Distance(0, 0, 0, 0, -1000000, 0), 1000));
        Check("distance: XYZ units", () => Close(ElectricalMath.Distance(0, 0, 1, 3000, 4000, 13), 13));
        Check("distance: widen before int subtraction", () => Assert(ElectricalMath.Distance(int.MinValue, 0, 0, int.MaxValue, 0, 0) > 4294960));
        Check("substation: rejects invalid limits", () => Throws<ArgumentOutOfRangeException>(() => new SubstationState(1650, 0, false)));
        Check("substation: rejects infinite current", () => Throws<ArgumentOutOfRangeException>(() => new SubstationState(1650, 100, false).AddLoad(float.PositiveInfinity)));
        Check("substation: one load commit per tick", () => { var s = new SubstationState(1650, 100, false); s.AddLoad(20); s.AddLoad(30); s.Tick(1, 1); Close(s.Load, 50); s.Tick(1, 1); Close(s.Load, 0); });
        Check("substation: sustained overload trips", () => { Trip(); });
        Check("substation: first restore at 10 game seconds", () => { var s = Trip(); s.Tick(9, 1); Assert(s.IsTripped); s.Tick(1, 1); Assert(!s.IsTripped); Close(s.Voltage, 1650); });
        Check("substation: repeated trip uses 30 seconds", () => { var s = Trip(); s.Tick(10, 1); for (int i = 0; i < 10 && !s.IsTripped; i++) { s.AddLoad(200); s.Tick(1, 0); } Assert(s.IsTripped); s.Tick(29, 1); Assert(s.IsTripped); s.Tick(1, 1); Assert(!s.IsTripped); });
        Check("substation: a new world starts untripped", () => { Trip(); var s = new SubstationState(1650, 100, false); Assert(!s.IsTripped); Close(s.Voltage, 1650); });
        Check("CSV: BOM CRLF empty fields", () => { var t=CsvTable.Read("\uFEFFa,b,c\r\n1,,3\r\n"); Assert(t.Count==2 && t[1][1]==""); });
        Check("CSV: comma and escaped quotes", () => { var t=CsvTable.Read("a,b\n\"x,y\",\"a\"\"b\"\n"); Assert(t[1][0]=="x,y" && t[1][1]=="a\"b"); });
        Check("CSV: multiline quoted field", () => Assert(CsvTable.Read("a\n\"b\nc\"\n")[1][0]=="b\nc"));
        Check("CSV: unterminated quote rejected", () => Throws<FormatException>(() => CsvTable.Read("a\n\"oops")));
        Check("CSV: data after quote rejected", () => Throws<FormatException>(() => CsvTable.Read("a\n\"x\"y")));
        Check("CSV row struct: whitespace and TRUE", () => { var t=new CSV_struct<Row>("Name,Enabled,Value\na, TRUE , 1.5\n"); Assert(t.get_row(1).Enabled); Close(t.get_row(1).Value,1.5f); });
        Check("CSV row struct: index zero rejected", () => Throws<ArgumentOutOfRangeException>(() => new CSV_struct<Row>("a,b,c\nx,true,1\n").get_row(0)));
        Check("CSV row struct: non-finite rejected", () => Throws<ArgumentException>(() => new CSV_struct<Row>("a,b,c\nx,true,NaN\n")));
        Console.WriteLine($"Core logic: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }
    public struct Row
    {
        [CSV_column(0)] public string Name;
        [CSV_column(1)] public bool Enabled;
        [CSV_column(2)] public float Value;
    }
}
namespace CatenaryDC { internal static class Main { internal static void release_log(string text) => Console.WriteLine(text); } }
