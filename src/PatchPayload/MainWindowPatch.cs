using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Forms;

namespace SMIDIToKey.KeyBoard.Entity
{
    // Compile-time shape only. The patcher maps this member to MIDIToKey's own type.
    public sealed class EHookStruct
    {
        public int VKCode { get; set; }
    }
}

namespace SMIDIToKey.MIDI
{
    // Compile-time shape only. No implementation from MIDIToKey is distributed.
    public sealed class MIDIManage
    {
        public bool IsCanMappingKeyToMIDI => throw new NotSupportedException();
        public void KeyDown(int channel, int velocity, int note) => throw new NotSupportedException();
        public void KeyUp(int channel, int note) => throw new NotSupportedException();
    }
}

namespace SMIDIToKey.SetupWindow.Setup
{
    // Compile-time shape only. The real Value getter is resolved in the user's assembly.
    public sealed class SetupIsKeyBoardToMIDI
    {
        public bool Value => throw new NotSupportedException();
    }
}

namespace SMIDIToKey
{
    using SMIDIToKey.KeyBoard.Entity;
    using SMIDIToKey.MIDI;
    using SMIDIToKey.SetupWindow.Setup;

    /// <summary>
    /// Independently written two-hand QWERTY piano behavior. dnlib transplants these
    /// method bodies into the user's legally installed MIDIToKey 2.8 executable.
    /// </summary>
    public class MainWindow : Window
    {
#pragma warning disable CS0649
        private SetupIsKeyBoardToMIDI setupIsKeyBoardToMIDI = null!;
        private MIDIManage midiManage = null!;
        private readonly HashSet<int> pressedKeys = new();
#pragma warning restore CS0649

        private readonly HashSet<int> pressedOctaveKeys = new();
        private readonly HashSet<int> tapEligibleOctaveKeys = new();
        private readonly HashSet<int> passthroughKeyboardKeys = new();
        private readonly Dictionary<int, List<int>> activeKeyboardNotes = new();
        private int lowerHandBaseNote = 48;
        private int upperHandBaseNote = 60;

        private void KeyPress(EHookStruct hookStruct, bool isDown, out bool handle)
        {
            // Never block Windows input. MIDIToKey listens in parallel with the foreground app.
            handle = false;
            if (!setupIsKeyBoardToMIDI.Value || !midiManage.IsCanMappingKeyToMIDI)
            {
                return;
            }

            var virtualKey = hookStruct.VKCode;
            var key = (Keys)virtualKey;

            if (isDown)
            {
                // Ignore keyboard auto-repeat. Each physical press starts at most one note/action.
                if (!pressedKeys.Add(virtualKey))
                {
                    return;
                }

                if (IsOctaveKey(key))
                {
                    // A modifier changes an octave only when it was tapped by itself.
                    if (pressedOctaveKeys.Count == 0)
                    {
                        tapEligibleOctaveKeys.Add(virtualKey);
                    }
                    else
                    {
                        tapEligibleOctaveKeys.Clear();
                    }
                    pressedOctaveKeys.Add(virtualKey);
                    return;
                }

                if (ShouldPassThroughKeyboardKey(key))
                {
                    // Ctrl+A, Shift+Q, Alt+Tab, and similar chords remain normal Windows input.
                    tapEligibleOctaveKeys.Clear();
                    passthroughKeyboardKeys.Add(virtualKey);
                    return;
                }

                var notes = ResolveKeyboardNotes(key);
                if (notes.Count == 0)
                {
                    return;
                }

                activeKeyboardNotes[virtualKey] = notes;
                foreach (var note in notes)
                {
                    midiManage.KeyDown(0, 127, note);
                }
                return;
            }

            if (!pressedKeys.Remove(virtualKey))
            {
                return;
            }

            if (IsOctaveKey(key))
            {
                var changeOctave = tapEligibleOctaveKeys.Remove(virtualKey);
                pressedOctaveKeys.Remove(virtualKey);
                if (changeOctave)
                {
                    ChangeOctave(key);
                    UpdateTwoHandIndicator();
                }

                if (pressedOctaveKeys.Count == 0)
                {
                    tapEligibleOctaveKeys.Clear();
                }
                return;
            }

            if (passthroughKeyboardKeys.Remove(virtualKey))
            {
                return;
            }

            if (!activeKeyboardNotes.TryGetValue(virtualKey, out var activeNotes))
            {
                return;
            }

            foreach (var note in activeNotes)
            {
                midiManage.KeyUp(0, note);
            }
            activeKeyboardNotes.Remove(virtualKey);
        }

        private static bool IsOctaveKey(Keys key)
        {
            return key == Keys.LShiftKey ||
                   key == Keys.RShiftKey ||
                   key == Keys.LControlKey ||
                   key == Keys.RControlKey;
        }

        private bool ShouldPassThroughKeyboardKey(Keys key)
        {
            if (IsOctaveKey(key))
            {
                return false;
            }

            var modifiers = Control.ModifierKeys;
            return pressedOctaveKeys.Count > 0 ||
                   (modifiers & (Keys.Control | Keys.Shift | Keys.Alt)) != Keys.None;
        }

        private List<int> ResolveKeyboardNotes(Keys key)
        {
            var upperOffset = GetUpperHandOffset(key);
            if (upperOffset >= 0)
            {
                return new List<int> { upperHandBaseNote + upperOffset };
            }

            var lowerOffset = GetLowerHandOffset(key);
            if (lowerOffset >= 0)
            {
                return new List<int> { lowerHandBaseNote + lowerOffset };
            }

            return new List<int>();
        }

        private static int GetUpperHandOffset(Keys key)
        {
            switch (key)
            {
                case Keys.Q: return 0;
                case Keys.D2: return 1;
                case Keys.W: return 2;
                case Keys.D3: return 3;
                case Keys.E: return 4;
                case Keys.R: return 5;
                case Keys.D5: return 6;
                case Keys.T: return 7;
                case Keys.D6: return 8;
                case Keys.Y: return 9;
                case Keys.D7: return 10;
                case Keys.U: return 11;
                default: return -1;
            }
        }

        private static int GetLowerHandOffset(Keys key)
        {
            switch (key)
            {
                case Keys.Z: return 0;
                case Keys.S: return 1;
                case Keys.X: return 2;
                case Keys.D: return 3;
                case Keys.C: return 4;
                case Keys.V: return 5;
                case Keys.G: return 6;
                case Keys.B: return 7;
                case Keys.H: return 8;
                case Keys.N: return 9;
                case Keys.J: return 10;
                case Keys.M: return 11;
                default: return -1;
            }
        }

        private void ChangeOctave(Keys key)
        {
            const int lowestBaseNote = 24; // C1
            const int highestBaseNote = 96; // C7

            switch (key)
            {
                case Keys.LShiftKey:
                    upperHandBaseNote -= 12;
                    if (upperHandBaseNote < lowestBaseNote) upperHandBaseNote = lowestBaseNote;
                    break;
                case Keys.RShiftKey:
                    upperHandBaseNote += 12;
                    if (upperHandBaseNote > highestBaseNote) upperHandBaseNote = highestBaseNote;
                    break;
                case Keys.LControlKey:
                    lowerHandBaseNote -= 12;
                    if (lowerHandBaseNote < lowestBaseNote) lowerHandBaseNote = lowestBaseNote;
                    break;
                case Keys.RControlKey:
                    lowerHandBaseNote += 12;
                    if (lowerHandBaseNote > highestBaseNote) lowerHandBaseNote = highestBaseNote;
                    break;
            }
        }

        private static string FormatOctaveRange(int baseNote)
        {
            var octave = baseNote / 12 - 1;
            return $"C{octave}-B{octave}";
        }

        private void UpdateTwoHandIndicator()
        {
            Title = $"SMIDIToKey [左手 {FormatOctaveRange(lowerHandBaseNote)} | 右手 {FormatOctaveRange(upperHandBaseNote)}]";
        }
    }
}
