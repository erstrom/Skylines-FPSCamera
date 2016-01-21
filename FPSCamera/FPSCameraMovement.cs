using ColossalFramework;
using ColossalFramework.Math;
using ICities;
using UnityEngine;
using System.Runtime.InteropServices;

namespace FPSCamera
{
    class FPSCameraMovement
    {
        static class NativeFuncs
        {
            [DllImport("xinput1_3.dll")]
            public static extern uint XInputGetState(uint playerIndex, out XInputState.XInputStateRaw state);
        }

        private struct XInputState
        {
            /* Deadzones as defined in Xinput.h */
            public const short XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE = 7849;
            public const short XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE = 8689;
            public const byte XINPUT_GAMEPAD_TRIGGER_THRESHOLD = 30;

            /* Axis normalization constants */
            public const short LEFT_AXIS_NORM = (0x7FFF - XInputState.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE);
            public const short RIGHT_AXIS_NORM = (0x7FFF - XInputState.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE);

            /* Button bitmasks for wButtons */
            public const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
            public const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
            public const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
            public const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
            public const ushort XINPUT_GAMEPAD_START = 0x0010;
            public const ushort XINPUT_GAMEPAD_BACK = 0x0020;
            public const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
            public const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
            public const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
            public const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
            public const ushort XINPUT_GAMEPAD_A = 0x1000;
            public const ushort XINPUT_GAMEPAD_B = 0x2000;
            public const ushort XINPUT_GAMEPAD_X = 0x4000;
            public const ushort XINPUT_GAMEPAD_Y = 0x8000;

            [StructLayout(LayoutKind.Sequential)]
            internal struct XInputStateRaw
            {
                public uint dwPacketNumber;
                public GamePad Gamepad;

                [StructLayout(LayoutKind.Sequential)]
                public struct GamePad
                {
                    public ushort wButtons;
                    public byte bLeftTrigger;
                    public byte bRightTrigger;
                    public short sThumbLX;
                    public short sThumbLY;
                    public short sThumbRX;
                    public short sThumbRY;
                }
            }
        }

        private class ToggleFuncStateMachine
        {
            private enum State
            {
                TriggerInactiveFuncInactive,
                TriggerActiveFuncInactive,
                TriggerInactiveFuncActive,
                TriggerActiveFuncActive
            }

            private State state;

            public ToggleFuncStateMachine()
            {
                state = State.TriggerInactiveFuncInactive;
            }

            public void updateState(bool triggerActive)
            {
                switch (state)
                {
                    case State.TriggerInactiveFuncInactive:
                        if (triggerActive)
                        {
                            state = State.TriggerActiveFuncActive;
                        }
                        break;
                    case State.TriggerActiveFuncActive:
                        if (!triggerActive)
                        {
                            state = State.TriggerInactiveFuncActive;
                        }
                        break;
                    case State.TriggerInactiveFuncActive:
                        if (triggerActive)
                        {
                            state = State.TriggerActiveFuncInactive;
                        }
                        break;
                    case State.TriggerActiveFuncInactive:
                        if (!triggerActive)
                        {
                            state = State.TriggerInactiveFuncInactive;
                        }
                        break;
                    default:
                        state = State.TriggerInactiveFuncInactive;
                        break;
                }
            }

            public bool Output
            {
                get
                {
                    if (state == State.TriggerInactiveFuncInactive ||
                        state == State.TriggerActiveFuncInactive)
                    {
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
            }
        }

        private class MovementStateMachine
        {
            /* Thresholds for movement state changes */
            private const float movementThresholdUp = 0.9f;
            private const float movementThresholdLow = 0.2f;
            private const float transitionTimeThreshold = 0.1f;

            private enum State
            {
                Init,
                FirstFastUpTransition,
                FirstFastLowTransition,
                FastMove
            }

            private State state;
            private float savedTime;
            private float upTime;
            private float lowTime;
            private Configuration config;

            public MovementStateMachine(Configuration config)
            {
                state = State.Init;
                savedTime = upTime = lowTime = 0;
                this.config = config;
            }

            public void updateState(float gamePadLy, float elapsedTime)
            {
                if (gamePadLy < movementThresholdUp && gamePadLy > movementThresholdLow)
                {
                    /* The movement state should not be updated if the left stick is 
                    in between the two thresholds. */
                    return;
                }
                float deltaTime = elapsedTime - savedTime;
                savedTime = elapsedTime;
                switch (state)
                {
                    case State.Init:
                        if (gamePadLy > movementThresholdUp && deltaTime < config.controllerDoubleTapInterval / 1000)
                        {
                            //Log.Message("Init -> FastUpTransition "+deltaTime);
                            state = State.FirstFastUpTransition;
                            upTime = elapsedTime;
                        }
                        break;
                    case State.FirstFastUpTransition:
                        float upTimeDelta = elapsedTime - upTime;
                        if (gamePadLy < movementThresholdLow && deltaTime < config.controllerDoubleTapInterval / 1000)
                        {
                            //Log.Message("FastUpTransition -> FastLowTransition " + deltaTime);
                            state = State.FirstFastLowTransition;
                            lowTime = elapsedTime;
                        }
                        else if (gamePadLy > movementThresholdLow && upTimeDelta > config.controllerDoubleTapInterval / 1000 ||
                                 deltaTime > config.controllerDoubleTapInterval / 1000)
                        {
                            //Log.Message("FastUpTransition -> Init " + deltaTime);
                            state = State.Init;
                        }
                        break;
                    case State.FirstFastLowTransition:
                        float lowTimeDelta = elapsedTime - lowTime;
                        if (gamePadLy > movementThresholdUp && deltaTime < config.controllerDoubleTapInterval / 1000)
                        {
                            //Log.Message("FastLowTransition -> FastMove " + deltaTime);
                            state = State.FastMove;
                        }
                        else if (gamePadLy < movementThresholdUp && lowTimeDelta > config.controllerDoubleTapInterval / 1000 ||
                                 deltaTime > config.controllerDoubleTapInterval / 1000)
                        {
                            //Log.Message("FastLowTransition -> Init " + deltaTime);
                            state = State.Init;
                        }
                        break;
                    case State.FastMove:
                        if (gamePadLy < movementThresholdUp)
                        {
                            //Log.Message("FastMove -> Init " + deltaTime);
                            state = State.Init;
                        }
                        break;
                    default:
                        state = State.Init;
                        break;
                }
            }

            public bool Output
            {
                get
                {
                    if (state == State.FastMove)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }

        private SavedInputKey cameraMoveLeft;
        private SavedInputKey cameraMoveRight;
        private SavedInputKey cameraMoveForward;
        private SavedInputKey cameraMoveBackward;
        private SavedInputKey cameraZoomCloser;
        private SavedInputKey cameraZoomAway;

        private ToggleFuncStateMachine showCursorSm;
        private ToggleFuncStateMachine pauseGameSm;
        private MovementStateMachine movementSm;

        private Configuration config;
        GameObject gameObject;
        private const float gamePadRightStickBaseAmplification = 30f;
        private float elapsedTime;

        public FPSCameraMovement(Configuration config, GameObject gameObject)
        {
            cameraMoveLeft = new SavedInputKey(Settings.cameraMoveLeft, Settings.gameSettingsFile, DefaultSettings.cameraMoveLeft, true);
            cameraMoveRight = new SavedInputKey(Settings.cameraMoveRight, Settings.gameSettingsFile, DefaultSettings.cameraMoveRight, true);
            cameraMoveForward = new SavedInputKey(Settings.cameraMoveForward, Settings.gameSettingsFile, DefaultSettings.cameraMoveForward, true);
            cameraMoveBackward = new SavedInputKey(Settings.cameraMoveBackward, Settings.gameSettingsFile, DefaultSettings.cameraMoveBackward, true);
            cameraZoomCloser = new SavedInputKey(Settings.cameraZoomCloser, Settings.gameSettingsFile, DefaultSettings.cameraZoomCloser, true);
            cameraZoomAway = new SavedInputKey(Settings.cameraZoomAway, Settings.gameSettingsFile, DefaultSettings.cameraZoomAway, true);

            showCursorSm = new ToggleFuncStateMachine();
            pauseGameSm = new ToggleFuncStateMachine();
            movementSm = new MovementStateMachine(config);
            this.config = config;
            this.gameObject = gameObject;
            elapsedTime = 0;
        }

        private XInputState.XInputStateRaw getControllerInput()
        {
            try
            {
                XInputState.XInputStateRaw state;
                uint rc = NativeFuncs.XInputGetState(config.controllerNumber, out state);
                if (rc != 0)
                {
                    state.Gamepad.sThumbLX = 0;
                    state.Gamepad.sThumbLY = 0;
                    state.Gamepad.sThumbRX = 0;
                    state.Gamepad.sThumbRY = 0;
                    state.Gamepad.wButtons = 0;
                }
                else
                {
                    // Compensate Left X axle
                    if (state.Gamepad.sThumbLX < -XInputState.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE)
                    {
                        state.Gamepad.sThumbLX += XInputState.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE;
                    }
                    else if (state.Gamepad.sThumbLX > XInputState.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE)
                    {
                        state.Gamepad.sThumbLX -= XInputState.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE;
                    }
                    else
                    {
                        state.Gamepad.sThumbLX = 0;
                    }

                    // Compensate Left Y axle
                    if (state.Gamepad.sThumbLY < -XInputState.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE)
                    {
                        state.Gamepad.sThumbLY += XInputState.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE;
                    }
                    else if (state.Gamepad.sThumbLY > XInputState.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE)
                    {
                        state.Gamepad.sThumbLY -= XInputState.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE;
                    }
                    else
                    {
                        state.Gamepad.sThumbLY = 0;
                    }

                    // Compensate Right X axle
                    if (state.Gamepad.sThumbRX < -XInputState.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE)
                    {
                        state.Gamepad.sThumbRX += XInputState.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE;
                    }
                    else if (state.Gamepad.sThumbRX > XInputState.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE)
                    {
                        state.Gamepad.sThumbRX -= XInputState.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE;
                    }
                    else
                    {
                        state.Gamepad.sThumbRX = 0;
                    }

                    // Compensate Right Y axle
                    if (state.Gamepad.sThumbRY < -XInputState.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE)
                    {
                        state.Gamepad.sThumbRY += XInputState.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE;
                    }
                    else if (state.Gamepad.sThumbRY > XInputState.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE)
                    {
                        state.Gamepad.sThumbRY -= XInputState.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE;
                    }
                    else
                    {
                        state.Gamepad.sThumbRY = 0;
                    }
                }
                return state;
            }
            catch (System.DllNotFoundException e)
            {
                Log.Error(e.Message);
                return new XInputState.XInputStateRaw();
            }
        }

        private void NormalizeControllerStickData(XInputState.XInputStateRaw state,
            out float gamePadLx, out float gamePadLy, out float gamePadRx, out float gamePadRy)
        {
            gamePadLx = ((float)state.Gamepad.sThumbLX) / XInputState.LEFT_AXIS_NORM;
            gamePadLy = ((float)state.Gamepad.sThumbLY) / XInputState.LEFT_AXIS_NORM;
            gamePadRx = ((float)state.Gamepad.sThumbRX) / XInputState.RIGHT_AXIS_NORM;
            gamePadRy = ((float)state.Gamepad.sThumbRY) / XInputState.RIGHT_AXIS_NORM;
        }

        public Vector3 GetFollowCameraOffset()
        {
            Vector3 offset = Vector3.zero;
            if (config.useController)
            {
                float gamePadLx, gamePadLy, gamePadRx, gamePadRy;
                XInputState.XInputStateRaw state = getControllerInput();
                NormalizeControllerStickData(state, out gamePadLx, out gamePadLy, out gamePadRx, out gamePadRy);
                
                offset += gameObject.transform.forward * config.cameraMoveSpeed * 0.25f * Time.deltaTime * gamePadLy;
                offset += gameObject.transform.right * config.cameraMoveSpeed * 0.25f * Time.deltaTime * gamePadLx;

                if ((state.Gamepad.wButtons & XInputState.XINPUT_GAMEPAD_A) == XInputState.XINPUT_GAMEPAD_A)
                {
                    offset += gameObject.transform.up * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
                else if ((state.Gamepad.wButtons & XInputState.XINPUT_GAMEPAD_RIGHT_THUMB) == XInputState.XINPUT_GAMEPAD_RIGHT_THUMB)
                {
                    offset -= gameObject.transform.up * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
            }
            else
            {
                if (cameraMoveForward.IsPressed())
                {
                    offset += gameObject.transform.forward * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
                else if (cameraMoveBackward.IsPressed())
                {
                    offset -= gameObject.transform.forward * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }

                if (cameraMoveLeft.IsPressed())
                {
                    offset -= gameObject.transform.right * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
                else if (cameraMoveRight.IsPressed())
                {
                    offset += gameObject.transform.right * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }

                if (cameraZoomAway.IsPressed())
                {
                    offset -= gameObject.transform.up * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
                else if (cameraZoomCloser.IsPressed())
                {
                    offset += gameObject.transform.up * config.cameraMoveSpeed * 0.25f * Time.deltaTime;
                }
            }
            return offset;
        }

        public void updateFpsCameraPosition(float speedFactor, ref float rotationY)
        {
            if (config.useController)
            {
                float gamePadLx, gamePadLy, gamePadRx, gamePadRy;
                XInputState.XInputStateRaw state = getControllerInput();
                NormalizeControllerStickData(state, out gamePadLx, out gamePadLy, out gamePadRx, out gamePadRy);
                
                movementSm.updateState(gamePadLy, elapsedTime);
                elapsedTime += Time.deltaTime;

                if (movementSm.Output)
                {
                    speedFactor *= /*config.goFasterSpeedMultiplier*/ 10;
                }

                Vector3 forward = gameObject.transform.forward;
                if (!movementSm.Output)
                {
                    forward.y = 0f;
                    forward.Normalize();
                }
                gameObject.transform.position += forward * config.cameraMoveSpeed * speedFactor * Time.deltaTime * gamePadLy;
                gameObject.transform.position += gameObject.transform.right * config.cameraMoveSpeed * speedFactor * Time.deltaTime * gamePadLx;

                if ((state.Gamepad.wButtons & XInputState.XINPUT_GAMEPAD_A) == XInputState.XINPUT_GAMEPAD_A)
                {
                    gameObject.transform.position += Vector3.up * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }
                else if ((state.Gamepad.wButtons & XInputState.XINPUT_GAMEPAD_RIGHT_THUMB) == XInputState.XINPUT_GAMEPAD_RIGHT_THUMB)
                {
                    gameObject.transform.position -= Vector3.up * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }
                showCursorSm.updateState((state.Gamepad.wButtons & XInputState.XINPUT_GAMEPAD_X) == XInputState.XINPUT_GAMEPAD_X);
                Cursor.visible = showCursorSm.Output;

                pauseGameSm.updateState((state.Gamepad.wButtons & XInputState.XINPUT_GAMEPAD_Y) == XInputState.XINPUT_GAMEPAD_Y);
                Singleton<SimulationManager>.instance.SimulationPaused = pauseGameSm.Output;

                float rotationX = gameObject.transform.localEulerAngles.y + gamePadRx * gamePadRightStickBaseAmplification * config.cameraRotationSensitivity * Time.deltaTime;
                rotationY += gamePadRy * gamePadRightStickBaseAmplification * config.cameraRotationSensitivity * Time.deltaTime * (config.invertYAxis ? -1.0f : 1.0f);
                if (rotationY > 89f)
                    rotationY = 89f;
                else if (rotationY < -89f)
                    rotationY = -89f;
                gameObject.transform.localEulerAngles = new Vector3(-rotationY, rotationX, 0);
            }
            else
            {
                if (Input.GetKey(config.goFasterHotKey))
                {
                    speedFactor *= config.goFasterSpeedMultiplier;
                }

                if (cameraMoveForward.IsPressed())
                {
                    gameObject.transform.position += gameObject.transform.forward * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }
                else if (cameraMoveBackward.IsPressed())
                {
                    gameObject.transform.position -= gameObject.transform.forward * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }

                if (cameraMoveLeft.IsPressed())
                {
                    gameObject.transform.position -= gameObject.transform.right * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }
                else if (cameraMoveRight.IsPressed())
                {
                    gameObject.transform.position += gameObject.transform.right * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }

                if (cameraZoomAway.IsPressed())
                {
                    gameObject.transform.position -= gameObject.transform.up * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }
                else if (cameraZoomCloser.IsPressed())
                {
                    gameObject.transform.position += gameObject.transform.up * config.cameraMoveSpeed * speedFactor * Time.deltaTime;
                }

                if (Input.GetKey(config.showMouseHotkey))
                {
                    Cursor.visible = true;
                }
                else
                {
                    float rotationX = gameObject.transform.localEulerAngles.y + Input.GetAxis("Mouse X") * config.cameraRotationSensitivity;
                    rotationY += Input.GetAxis("Mouse Y") * config.cameraRotationSensitivity * (config.invertYAxis ? -1.0f : 1.0f);
                    gameObject.transform.localEulerAngles = new Vector3(-rotationY, rotationX, 0);
                    Cursor.visible = false;
                }
            }
        }
    }
}
