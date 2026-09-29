// Assertions for the tests. A failed check throws CheckFailed with the expected
// and the actual value, which the runner prints.
using System;
using System.Collections;
using System.Collections.Generic;

class CheckFailed : Exception
{
    public CheckFailed(string message) : base(message) { }
}

// Thrown by a test that can't run here (no such device, not the right setup).
class SkipTest : Exception
{
    public SkipTest(string why) : base(why) { }
}

static class Check
{
    public static void Equal<T>(T expected, T actual) { Equal(expected, actual, null); }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (object.Equals(expected, actual)) return;
        throw new CheckFailed(Label(what) + "expected " + Show(expected) + ", got " + Show(actual));
    }

    public static void True(bool value, string what)
    {
        if (!value) throw new CheckFailed(Label(what) + "expected true, got false");
    }

    public static void False(bool value, string what)
    {
        if (value) throw new CheckFailed(Label(what) + "expected false, got true");
    }

    public static void Null(object value, string what)
    {
        if (value != null) throw new CheckFailed(Label(what) + "expected null, got " + Show(value));
    }

    public static void NotNull(object value, string what)
    {
        if (value == null) throw new CheckFailed(Label(what) + "expected a value, got null");
    }

    // text contains part (ordinal, case-sensitive)
    public static void Contains(string part, string text)
    {
        if (text != null && part != null && text.IndexOf(part, StringComparison.Ordinal) >= 0) return;
        throw new CheckFailed("expected " + Show(text) + " to contain " + Show(part));
    }

    public static void DoesNotContain(string part, string text)
    {
        if (text == null || part == null || text.IndexOf(part, StringComparison.Ordinal) < 0) return;
        throw new CheckFailed("expected " + Show(text) + " not to contain " + Show(part));
    }

    public static void Contains<T>(T item, IEnumerable<T> list)
    {
        foreach (T x in list) if (object.Equals(x, item)) return;
        throw new CheckFailed("expected " + Show(list) + " to contain " + Show(item));
    }

    public static TEx Throws<TEx>(Action action) where TEx : Exception
    {
        try { action(); }
        catch (TEx ex) { return ex; }
        catch (Exception ex) { throw new CheckFailed("expected " + typeof(TEx).Name + ", got " + ex.GetType().Name + ": " + ex.Message); }
        throw new CheckFailed("expected " + typeof(TEx).Name + ", but nothing was thrown");
    }

    static string Label(string what) { return what == null ? "" : what + ": "; }

    public static string Show(object v)
    {
        if (v == null) return "null";
        if (v is string) return "\"" + v + "\"";
        if (v is IEnumerable)
        {
            var parts = new List<string>();
            foreach (object x in (IEnumerable)v) parts.Add(Show(x));
            return "[" + string.Join(", ", parts.ToArray()) + "]";
        }
        return v.ToString();
    }
}
