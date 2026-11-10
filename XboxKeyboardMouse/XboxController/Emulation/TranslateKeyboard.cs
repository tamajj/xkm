using SimWinInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using XboxKeyboardMouse.Libs;

namespace XboxKeyboardMouse {
    class TranslateKeyboard {
        public enum TriggerType { LeftTrigger, RightTrigger }

        public static void ClearAllDicts()
        {
            mapLeftStickX.Clear();
            mapLeftStickY.Clear();
            mapRightStickX.Clear();
            mapRightStickY.Clear();
            buttons.Clear();
            triggers.Clear();
        }

        public static Dictionary<Key, short> mapLeftStickY = new Dictionary<Key, short>();
        public static Dictionary<Key, short> mapLeftStickX = new Dictionary<Key, short>();
        public static Dictionary<Key, short> mapRightStickX = new Dictionary<Key, short>();
        public static Dictionary<Key, short> mapRightStickY = new Dictionary<Key, short>();

        public static Dictionary<Key, GamePadControl> buttons = new Dictionary<Key, GamePadControl>();
        public static Dictionary<Key, TriggerType> triggers = new Dictionary<Key, TriggerType>();

        public static Dictionary<RunTimeOptionType, RunTimeOption> mapRunTimeOptions = new Dictionary<RunTimeOptionType, RunTimeOption>();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;

        /// <summary>Reads the physical state of a virtual key right now.</summary>
        /// <remarks>
        /// GetAsyncKeyState does not depend on the calling thread pumping window messages, unlike
        /// WPF's Keyboard.IsKeyDown (GetKeyState), which can report stale state on our polling threads.
        /// </remarks>
        public static bool IsVirtualKeyDown(int virtualKey)
        {
            return virtualKey != 0 && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        public static bool IsKeyDown(Key key)
        {
            if (key == Key.None) return false;
            return IsVirtualKeyDown(KeyInterop.VirtualKeyFromKey(key));
        }

        /// <summary>Applies keyboard and mouse-button state to the controller.</summary>
        /// <returns>True if anything on the controller actually changed and needs to be sent.</returns>
        private static bool KeyInput(SimulatedGamePadState controller) {
            bool changed = false;

            try {
                bool mouseDisabled = Program.ActiveConfig.Mouse_Eng_Type == MouseTranslationMode.NONE;

                // -------------------------------------------------------------------------------
                //                                STICKS
                // -------------------------------------------------------------------------------
                if (mouseDisabled || Program.ActiveConfig.Mouse_Is_RightStick)
                {
                    short x = StickValueFromKeyboardState(mapLeftStickX);
                    short y = StickValueFromKeyboardState(mapLeftStickY);
                    if (controller.LeftStickX != x) { controller.LeftStickX = x; changed = true; }
                    if (controller.LeftStickY != y) { controller.LeftStickY = y; changed = true; }
                }
                if (mouseDisabled || !Program.ActiveConfig.Mouse_Is_RightStick)
                {
                    short x = StickValueFromKeyboardState(mapRightStickX);
                    short y = StickValueFromKeyboardState(mapRightStickY);
                    if (controller.RightStickX != x) { controller.RightStickX = x; changed = true; }
                    if (controller.RightStickY != y) { controller.RightStickY = y; changed = true; }
                }

                // -------------------------------------------------------------------------------
                //                                BUTTONS
                // -------------------------------------------------------------------------------
                // Work out the complete button state first and write it once, so the shared controller
                // state never holds a half-updated value another thread could send.
                GamePadControl newButtons = controller.Buttons;
                foreach (var control in GamePadControls.BinaryControls)
                {
                    if (AnyKeyIsPressedForControl(buttons, control))
                        newButtons |= control;
                    else
                        newButtons &= ~control;
                }
                if (controller.Buttons != newButtons) { controller.Buttons = newButtons; changed = true; }

                // -------------------------------------------------------------------------------
                //                                TRIGGERS
                // -------------------------------------------------------------------------------
                // Left mouse button = right trigger, right mouse button = left trigger, plus any
                // keyboard keys mapped to a trigger. Either source holds the trigger down.
                bool rightDown = IsVirtualKeyDown(VK_LBUTTON);
                bool leftDown = IsVirtualKeyDown(VK_RBUTTON);
                foreach (KeyValuePair<Key, TriggerType> entry in triggers) {
                    if (!IsKeyDown(entry.Key)) continue;

                    if (entry.Value == TriggerType.RightTrigger)
                         rightDown = true;
                    else leftDown = true;
                }

                byte rightTrigger = rightDown ? (byte)255 : (byte)0;
                byte leftTrigger = leftDown ? (byte)255 : (byte)0;
                if (controller.RightTrigger != rightTrigger) { controller.RightTrigger = rightTrigger; changed = true; }
                if (controller.LeftTrigger != leftTrigger) { controller.LeftTrigger = leftTrigger; changed = true; }

                // -------------------------------------------------------------------------------
                //                                CALIBRATION / RUNTIME OPTIONS
                // -------------------------------------------------------------------------------
                // Calibration / runtime options will detect the first key-down state for their associated button, but 
                // will ignore further iterations where the button is still being held. This can be used to advance
                // through calibration states, or have ways to swap configurations on the fly, etc.
                foreach (var option in mapRunTimeOptions.Values)
                {
                    if (option == null) continue;

                    if (IsKeyDown(option.Key))
                    {
                        if (!option.Ran)
                        {
                            option.Run();
                            option.Ran = true;
                        }
                    }
                    else
                    {
                        option.Ran = false;
                    }
                }
            }
            catch (Exception) { /* This occures when changing presets */ }

            return changed;
        }

        private static short StickValueFromKeyboardState(Dictionary<Key, short> map)
        {
            return (from entry in map
                    where IsKeyDown(entry.Key)
                    select entry.Value).FirstOrDefault();
        }

        private static bool AnyKeyIsPressedForControl(Dictionary<Key, GamePadControl> map, GamePadControl control)
        {
            return map.Any(entry => entry.Value == control && IsKeyDown(entry.Key));
        }

        private static bool Debug_TimeTracer(SimulatedGamePadState Controller) {
            // Get the start time
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Call the key input
            bool changed = KeyInput(Controller);

            // Get the end time
            watch.Stop();
            string time = watch.ElapsedMilliseconds + " MS";
            
            if (time != "0 MS") {
                // Display the time
                Logger.appendLogLine("KeyboardInput", $"Timed @ {time}", Logger.Type.Debug);
            }

            return changed;
        }

        /// <returns>True if the controller state changed and should be sent.</returns>
        public static bool KeyboardInput(SimulatedGamePadState controller) =>
            #if (DEBUG)
                    // Only enable if you have timing issues AKA Latency on 
                    // the keyboard inputs
                    Debug_TimeTracer(controller);
                    //KeyInput(controller);
            #else
                    KeyInput(controller);
            #endif
    }
}
