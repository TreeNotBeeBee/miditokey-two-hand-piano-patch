using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class Program
{
    private static int checks;
    private static int failures;

    private static void Main(string[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("Expected the patched SMIDIToKey.exe path.");
        }

        var assembly = Assembly.LoadFrom(args[0]);
        var windowType = assembly.GetType("SMIDIToKey.MainWindow", throwOnError: true);
        var window = FormatterServices.GetUninitializedObject(windowType);

        var pressedOctaveKeys = new HashSet<int>();
        SetField(windowType, window, "pressedOctaveKeys", pressedOctaveKeys);
        SetField(windowType, window, "tapEligibleOctaveKeys", new HashSet<int>());
        SetField(windowType, window, "passthroughKeyboardKeys", new HashSet<int>());
        SetField(windowType, window, "activeKeyboardNotes", new Dictionary<int, List<int>>());
        SetField(windowType, window, "lowerHandBaseNote", 48);
        SetField(windowType, window, "upperHandBaseNote", 60);

        var resolve = GetMethod(windowType, "ResolveKeyboardNotes");
        var isOctaveKey = GetMethod(windowType, "IsOctaveKey", isStatic: true);
        var shouldPass = GetMethod(windowType, "ShouldPassThroughKeyboardKey");
        var changeOctave = GetMethod(windowType, "ChangeOctave");
        var formatRange = GetMethod(windowType, "FormatOctaveRange", isStatic: true);
        var keysType = resolve.GetParameters()[0].ParameterType;

        var expectedNotes = new Dictionary<int, int>
        {
            [81] = 60, [50] = 61, [87] = 62, [51] = 63,
            [69] = 64, [82] = 65, [53] = 66, [84] = 67,
            [54] = 68, [89] = 69, [55] = 70, [85] = 71,
            [90] = 48, [83] = 49, [88] = 50, [68] = 51,
            [67] = 52, [86] = 53, [71] = 54, [66] = 55,
            [72] = 56, [78] = 57, [74] = 58, [77] = 59
        };
        foreach (var pair in expectedNotes)
        {
            ExpectNote(resolve, window, keysType, pair.Key, pair.Value, $"VK {pair.Key}");
        }

        ExpectEmpty(resolve, window, keysType, 65, "A is not a piano key");
        ExpectEmpty(resolve, window, keysType, 52, "4 is not a black key");
        ExpectEmpty(resolve, window, keysType, 73, "I is outside the upper octave");

        CheckOctaveKey(isOctaveKey, keysType, 160, true, "Left Shift");
        CheckOctaveKey(isOctaveKey, keysType, 161, true, "Right Shift");
        CheckOctaveKey(isOctaveKey, keysType, 162, true, "Left Ctrl");
        CheckOctaveKey(isOctaveKey, keysType, 163, true, "Right Ctrl");
        CheckOctaveKey(isOctaveKey, keysType, 81, false, "Q");

        CheckPassThrough(shouldPass, window, keysType, 81, false, "Q without a modifier sounds");
        pressedOctaveKeys.Add(162);
        CheckPassThrough(shouldPass, window, keysType, 65, true, "Ctrl+A passes through");
        CheckPassThrough(shouldPass, window, keysType, 81, true, "Ctrl+Q passes through");
        pressedOctaveKeys.Clear();

        InvokeKeyMethod(changeOctave, window, keysType, 160);
        CheckIntField(windowType, window, "upperHandBaseNote", 48, "Left Shift lowers the upper row");
        InvokeKeyMethod(changeOctave, window, keysType, 161);
        CheckIntField(windowType, window, "upperHandBaseNote", 60, "Right Shift raises the upper row");
        InvokeKeyMethod(changeOctave, window, keysType, 162);
        CheckIntField(windowType, window, "lowerHandBaseNote", 36, "Left Ctrl lowers the lower row");
        InvokeKeyMethod(changeOctave, window, keysType, 163);
        CheckIntField(windowType, window, "lowerHandBaseNote", 48, "Right Ctrl raises the lower row");

        SetField(windowType, window, "lowerHandBaseNote", 24);
        InvokeKeyMethod(changeOctave, window, keysType, 162);
        CheckIntField(windowType, window, "lowerHandBaseNote", 24, "Lower row stops at C1");
        SetField(windowType, window, "upperHandBaseNote", 96);
        InvokeKeyMethod(changeOctave, window, keysType, 161);
        CheckIntField(windowType, window, "upperHandBaseNote", 96, "Upper row stops at C7");

        CheckFormat(formatRange, 24, "C1-B1");
        CheckFormat(formatRange, 60, "C4-B4");
        CheckFormat(formatRange, 96, "C7-B7");

        CheckField(windowType, "pressedOctaveKeys", typeof(HashSet<int>), "Pressed octave-key state");
        CheckField(windowType, "tapEligibleOctaveKeys", typeof(HashSet<int>), "Tap eligibility state");
        CheckField(windowType, "passthroughKeyboardKeys", typeof(HashSet<int>), "Shortcut pass-through state");
        CheckField(windowType, "activeKeyboardNotes", typeof(Dictionary<int, List<int>>), "Active note state");
        CheckField(windowType, "lowerHandBaseNote", typeof(int), "Lower-row octave state");
        CheckField(windowType, "upperHandBaseNote", typeof(int), "Upper-row octave state");
        CheckMethod(windowType, "GetUpperHandOffset", "Upper-row mapping method");
        CheckMethod(windowType, "GetLowerHandOffset", "Lower-row mapping method");
        CheckMethod(windowType, "UpdateTwoHandIndicator", "Window title indicator");

        if (failures != 0)
        {
            throw new InvalidOperationException($"{failures} of {checks} behavior checks failed.");
        }

        Console.WriteLine($"All {checks} two-hand keyboard behavior checks passed.");
    }

    private static MethodInfo GetMethod(Type type, string name, bool isStatic = false)
    {
        var flags = BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        return type.GetMethod(name, flags) ?? throw new MissingMethodException(type.FullName, name);
    }

    private static void SetField(Type type, object target, string name, object value)
    {
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, name);
        field.SetValue(target, value);
    }

    private static void ExpectNote(MethodInfo method, object target, Type keysType, int key, int note, string label)
    {
        var result = (List<int>)method.Invoke(target, new[] { Enum.ToObject(keysType, key) });
        Report(result.Count == 1 && result[0] == note, $"{label} -> {string.Join(",", result)}");
    }

    private static void ExpectEmpty(MethodInfo method, object target, Type keysType, int key, string label)
    {
        var result = (List<int>)method.Invoke(target, new[] { Enum.ToObject(keysType, key) });
        Report(result.Count == 0, $"{label} -> count {result.Count}");
    }

    private static void CheckOctaveKey(MethodInfo method, Type keysType, int key, bool expected, string label)
    {
        var actual = (bool)method.Invoke(null, new[] { Enum.ToObject(keysType, key) });
        Report(actual == expected, $"{label} is octave key = {actual}");
    }

    private static void CheckPassThrough(
        MethodInfo method,
        object target,
        Type keysType,
        int key,
        bool expected,
        string label)
    {
        var actual = (bool)method.Invoke(target, new[] { Enum.ToObject(keysType, key) });
        Report(actual == expected, $"{label} = {actual}");
    }

    private static void InvokeKeyMethod(MethodInfo method, object target, Type keysType, int key)
    {
        method.Invoke(target, new[] { Enum.ToObject(keysType, key) });
    }

    private static void CheckIntField(Type type, object target, string name, int expected, string label)
    {
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, name);
        var actual = (int)field.GetValue(target);
        Report(actual == expected, $"{label} -> {actual}");
    }

    private static void CheckFormat(MethodInfo method, int note, string expected)
    {
        var actual = (string)method.Invoke(null, new object[] { note });
        Report(actual == expected, $"Range {note} -> {actual}");
    }

    private static void CheckField(Type type, string name, Type expectedType, string label)
    {
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Report(field != null && field.FieldType == expectedType, $"{label} field");
    }

    private static void CheckMethod(Type type, string name, string label)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic);
        Report(method != null, $"{label} method");
    }

    private static void Report(bool ok, string message)
    {
        checks++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {message}");
        if (!ok)
        {
            failures++;
        }
    }
}
