using System;
using System.Diagnostics;
using System.Threading;
using Sandbox.Game;
using Sandbox.Game.Gui;
using Sandbox.Game.Entities.Character;
using Sandbox.ModAPI;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Input;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace HDRClientRenderer.Input
{
    // Construct and Update on the game thread from the optional
    // native client plugin. No Harmony patch, global input change, or world pause.
    public sealed class PointerProviderNative : IDisposable
    {
        public const long DiscoveryChannel = 481770140;
        public const long RegistrationChannel = 481770141;
        public const string Protocol = "HDR.Pointer/1";
        // Exclusive routing, normalized pointer, five mouse buttons, tail guardian,
        // fresh native USE activation, and generation-scoped revocable lease.
        public const int CapabilityMask = 63;
        readonly PointerLeasePolicy policy = new PointerLeasePolicy();
        readonly Func<string, object[], object> endpoint;
        readonly int gameThread;
        LeaseScreen screen;
        object session;
        long controlledEntity;
        MyCharacter capturedCharacter;
        object capturedWeapon;
        MyStringId capturedControlContext;
        long inputFrame;
        bool registered, disposed, removing;

        static readonly MyStringId[] ExitControls = {
            MyControlsSpace.SLOT0, MyControlsSpace.SLOT1, MyControlsSpace.SLOT2, MyControlsSpace.SLOT3,
            MyControlsSpace.SLOT4, MyControlsSpace.SLOT5, MyControlsSpace.SLOT6, MyControlsSpace.SLOT7,
            MyControlsSpace.SLOT8, MyControlsSpace.SLOT9, MyControlsSpace.TOOLBAR_UP, MyControlsSpace.TOOLBAR_DOWN,
            MyControlsSpace.TOOLBAR_NEXT_ITEM, MyControlsSpace.TOOLBAR_PREV_ITEM, MyControlsSpace.TOOLBAR_PREVIOUS,
            MyControlsSpace.TOOLBAR_NEXT, MyControlsSpace.BUILD_SCREEN, MyControlsSpace.TERMINAL,
            MyControlsSpace.INVENTORY, MyControlsSpace.CONTROL_MENU, MyControlsSpace.TOOLBAR_RADIAL_MENU,
            MyControlsSpace.MAIN_MENU, MyControlsSpace.PAUSE_GAME, MyControlsSpace.CHAT_SCREEN,
            MyControlsSpace.SPECTATOR_NONE, MyControlsSpace.SPECTATOR_DELTA, MyControlsSpace.SPECTATOR_FREE,
            MyControlsSpace.SPECTATOR_STATIC, MyControlsSpace.SPECTATOR_SWITCHMODE
        };

        public PointerProviderNative()
        {
            gameThread = Thread.CurrentThread.ManagedThreadId;
            endpoint = Invoke;
            MyScreenManager.ScreenAdded += ScreenAdded;
            MyScreenManager.ScreenRemoved += ScreenRemoved;
            MyScreenManager.EndOfDraw += GuardMembership;
        }
        public Func<string, object[], object> Endpoint { get { return endpoint; } }
        static double Now { get { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; } }
        bool OnGameThread { get { return Thread.CurrentThread.ManagedThreadId == gameThread; } }

        public void Update()
        {
            if (!OnGameThread) return;
            CheckMembership();
            if (!disposed && !registered && MyAPIGateway.Utilities != null && MyAPIGateway.Session != null &&
                !MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Utilities.RegisterMessageHandler(DiscoveryChannel, Discover);
                registered = true; Publish();
            }
            if (MyAPIGateway.Session == null || (session != null && !ReferenceEquals(session, MyAPIGateway.Session)))
            {
                policy.WorldGone(); RemoveOwnScreen();
                session = null; controlledEntity = 0; capturedCharacter = null; capturedWeapon = null;
                if (registered) Unregister();
                return;
            }
            policy.Update(Now, ReadEnvironment());
            FinishScreen();
        }

        void Discover(object message) { if (OnGameThread && !disposed && registered) Publish(); }
        void Publish()
        {
            MyAPIGateway.Utilities.SendModMessage(RegistrationChannel,
                new MyTuple<string, int, Func<string, object[], object>>(Protocol, 1, endpoint));
        }
        void Unregister()
        {
            if (!registered) return;
            registered = false;
            if (MyAPIGateway.Utilities != null)
            {
                try { MyAPIGateway.Utilities.UnregisterMessageHandler(DiscoveryChannel, Discover); } catch { }
                try { MyAPIGateway.Utilities.SendModMessage(RegistrationChannel,
                    new MyTuple<string, int, Func<string, object[], object>>(Protocol, 0, endpoint)); } catch { }
            }
        }

        object Invoke(string operation, object[] args)
        {
            if (!OnGameThread) return null;
            CheckMembership();
            if (operation == "version") return Protocol;
            if (operation == "capabilities")
                return new MyTuple<int, int, double>(1, disposed ? 0 : CapabilityMask, PointerLeasePolicy.PollTimeout);
            if (operation == "release" && Exact(args, 1) && args[0] is long)
            {
                bool result = policy.Release((long)args[0], Now, ReadEnvironment());
                FinishScreen(); return result;
            }
            if (operation == "sample" && Exact(args, 1) && args[0] is long)
            {
                var value = policy.Sample((long)args[0], Now, ReadEnvironment());
                FinishScreen();
                return new MyTuple<long, int, MyTuple<double, double>, MyTuple<int, int, int>>(value.Frame,
                    (int)value.Flags, new MyTuple<double, double>(value.X, value.Y),
                    new MyTuple<int, int, int>(value.Pressed, value.Held, value.Released));
            }
            if (disposed || !registered) return null;
            if (operation == "acquire" && Exact(args, 2) && args[0] is long && args[1] is string)
            {
                if (screen != null) return new MyTuple<long, int>(0, (int)PointerFlags.Unavailable);
                var environment = ReadEnvironment();
                long token = policy.Acquire((long)args[0], (string)args[1], Now, environment);
                if (token == 0) return new MyTuple<long, int>(0, (int)PointerFlags.Unavailable);
                session = MyAPIGateway.Session;
                controlledEntity = ControlledEntityId();
                capturedCharacter = Sandbox.Game.World.MySession.Static.ControlledEntity as MyCharacter;
                capturedWeapon = capturedCharacter == null ? null : capturedCharacter.CurrentWeapon;
                capturedControlContext = capturedCharacter.ControlContext;
                screen = new LeaseScreen(this, token);
                // Install synchronously while this native input frame is neutral.
                // Queued AddScreen could let gameplay start shooting before the
                // screen lands, then swallow that gameplay-owned release. In the
                // exclusive manager branch AddScreenNow is safe during Use input.
                // Installation/focus still produces no Applied acknowledgement.
                try { MyScreenManager.AddScreenNow(screen); }
                catch
                {
                    policy.Cancel(PointerCancelReason.ScreenLost, ReadEnvironment());
                    bool installed = false;
                    foreach (var current in MyScreenManager.Screens)
                        if (ReferenceEquals(current, screen)) { installed = true; break; }
                    if (!installed)
                    {
                        // RemoveScreen also cancels this exact pending addition.
                        // A never-routed screen has no mouse tail to own or guard.
                        screen.DisableFocus();
                        MyScreenManager.RemoveScreen(screen);
                        screen = null; removing = false; controlledEntity = 0;
                        capturedCharacter = null; capturedWeapon = null; policy.WorldGone();
                    }
                    return new MyTuple<long, int>(0, (int)(PointerFlags.Unavailable | PointerFlags.Cancelled));
                }
                return new MyTuple<long, int>(token, (int)PointerFlags.Pending);
            }
            return null;
        }
        static bool Exact(object[] args, int count) { return args != null && args.Length == count; }

        static long ControlledEntityId()
        {
            var world = Sandbox.Game.World.MySession.Static;
            return world == null || world.ControlledEntity == null || world.ControlledEntity.Entity == null
                ? 0 : world.ControlledEntity.Entity.EntityId;
        }
        PointerEnvironment ReadEnvironment()
        {
            var e = new PointerEnvironment { Frame = inputFrame };
            try
            {
                var input = MyInput.Static;
                var focus = MyScreenManager.GetScreenWithFocus();
                e.OwnFocused = screen != null && ReferenceEquals(focus, screen);
                e.GameplayFocused = focus is MyGuiScreenGamePlay;
                e.SharedInput = MyScreenManager.InputToNonFocusedScreens;
                e.OwnScreenClosing = screen != null &&
                    (screen.State == MyGuiScreenState.CLOSING || screen.State == MyGuiScreenState.CLOSED ||
                     screen.State == MyGuiScreenState.HIDDEN || screen.State == MyGuiScreenState.HIDING);
                e.WindowActive = MyVRage.Platform != null && MyVRage.Platform.Windows != null &&
                    MyVRage.Platform.Windows.Window != null && MyVRage.Platform.Windows.Window.IsActive;
                var player = MyAPIGateway.Session == null ? null : MyAPIGateway.Session.Player;
                var character = player == null ? null : player.Character as MyCharacter;
                long entity = ControlledEntityId();
                var world = Sandbox.Game.World.MySession.Static;
                // The entry firing-state proof is available for an on-foot native
                // character. Cockpit, remote and spectator controls are unsupported.
                e.PlayerReady = character != null && !character.IsDead && !character.Closed &&
                    world != null && ReferenceEquals(world.ControlledEntity, character) && entity == character.EntityId;
                if (controlledEntity != 0 && entity != controlledEntity) e.ToolExitPressed = true;
                if (capturedCharacter != null && (!ReferenceEquals(character, capturedCharacter) ||
                    !ReferenceEquals(character.CurrentWeapon, capturedWeapon))) e.ToolExitPressed = true;
                if (character != null)
                    e.ExistingToolShooting = character.IsShooting(MyShootActionEnum.PrimaryAction) ||
                        character.IsShooting(MyShootActionEnum.SecondaryAction);
                if (input == null) return e;
                e.Buttons = (input.IsLeftMousePressed() ? 1 : 0) | (input.IsRightMousePressed() ? 2 : 0) |
                    (input.IsMiddleMousePressed() ? 4 : 0) | (input.IsXButton1MousePressed() ? 8 : 0) |
                    (input.IsXButton2MousePressed() ? 16 : 0);
                // Death may clear ControlledEntity before the mouse/controller tail
                // is released. Retain the acquired on-foot control context to observe
                // physical neutral state without waiting for a respawn or a new owner.
                var context = world != null && world.ControlledEntity != null
                    ? world.ControlledEntity.ControlContext : capturedControlContext;
                // Gameplay uses this combined helper for mouse/keyboard plus
                // controller triggers; raw MyInput controls alone are insufficient.
                e.PrimaryToolHeld = input.IsGameControlPressed(MyControlsSpace.PRIMARY_TOOL_ACTION) ||
                    MyControllerHelper.IsControl(context, MyControlsSpace.PRIMARY_TOOL_ACTION,
                        MyControlStateType.PRESSED, false, true);
                e.SecondaryToolHeld = input.IsGameControlPressed(MyControlsSpace.SECONDARY_TOOL_ACTION) ||
                    MyControllerHelper.IsControl(context, MyControlsSpace.SECONDARY_TOOL_ACTION,
                        MyControlStateType.PRESSED, false, true);
                e.InputObserved = true;
                e.NeutralTrigger = input.IsNewGameControlPressed(MyControlsSpace.USE);
                e.EscapePressed = input.IsNewKeyPressed(MyKeys.Escape);
                for (int i = 0; i < ExitControls.Length && !e.ToolExitPressed; i++)
                    e.ToolExitPressed = input.IsNewGameControlPressed(ExitControls[i]);
                // GetMousePosition applies the engine's configurable mouse scale;
                // the visible software cursor is drawn from the raw pixel position.
                var area = input.GetMouseAreaSize(); var position = input.GetRawMousePosition();
                e.PointerValid = Finite(area.X) && Finite(area.Y) && area.X > 0 && area.Y > 0 &&
                    Finite(position.X) && Finite(position.Y);
                if (e.PointerValid)
                {
                    e.X = Math.Max(0, Math.Min(1, position.X / (double)area.X));
                    e.Y = Math.Max(0, Math.Min(1, position.Y / (double)area.Y));
                }
            }
            catch { e.PlayerReady = false; e.PointerValid = false; e.InputObserved = false; }
            return e;
        }
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        void RoutedInput(LeaseScreen sender)
        {
            if (!ReferenceEquals(screen, sender) || sender.Token != policy.Token) return;
            inputFrame++;
            var e = ReadEnvironment();
            // This is the sole capture acknowledgement path. Both facts are native
            // observations inside the screen's routed HandleInput, never assumptions.
            if (!e.OwnFocused || e.SharedInput)
                policy.Cancel(e.SharedInput ? PointerCancelReason.SharedInput : PointerCancelReason.ScreenLost, e);
            else policy.RoutedInput(Now, e);
            FinishScreen();
        }
        void LostInput(LeaseScreen sender)
        {
            if (ReferenceEquals(screen, sender)) policy.Cancel(PointerCancelReason.FocusLost, ReadEnvironment());
        }
        void CloseRequested(LeaseScreen sender)
        {
            if (ReferenceEquals(screen, sender)) policy.Cancel(PointerCancelReason.ScreenLost, ReadEnvironment());
        }
        void ScreenAdded(MyGuiScreenBase added)
        {
            if (!ReferenceEquals(added, screen)) CheckMembership();
            if (screen != null && !ReferenceEquals(added, screen))
                policy.Cancel(PointerCancelReason.ScreenLost, ReadEnvironment());
        }
        void GuardMembership()
        {
            // GUI LoadData clears lists/queues without removal notifications.
            // The GUI draw lifecycle still runs after a disposed plugin stops
            // updating, so an orphaned tail can detach its event subscriptions.
            if (OnGameThread) CheckMembership();
        }
        void CheckMembership()
        {
            if (screen == null) return;
            foreach (var current in MyScreenManager.Screens)
                if (ReferenceEquals(current, screen)) return;
            policy.WorldGone(); screen = null; removing = false;
            controlledEntity = 0; capturedCharacter = null; capturedWeapon = null;
            DetachIfDisposed();
        }
        void ScreenRemoved(MyGuiScreenBase removed)
        {
            if (!ReferenceEquals(removed, screen)) return;
            if (!removing) policy.Cancel(PointerCancelReason.ScreenLost, ReadEnvironment());
            screen = null;
            removing = false;
            // Never re-add a forcibly removed screen or manufacture fresh focus.
            policy.WorldGone(); controlledEntity = 0; capturedCharacter = null; capturedWeapon = null;
            DetachIfDisposed();
        }
        void FinishScreen()
        {
            if (!policy.NeedsScreen) RemoveOwnScreen();
            DetachIfDisposed();
        }
        void RemoveOwnScreen()
        {
            if (screen == null || removing) return;
            removing = true;
            screen.AllowRemoval = true;
            screen.CloseScreenNow(false);
            MyScreenManager.RemoveScreen(screen);
            // ScreenRemoved supplies the final reference cleanup after queued removal.
        }
        void ScreenTick(LeaseScreen sender)
        {
            if (!ReferenceEquals(screen, sender)) return;
            if (MyAPIGateway.Session == null || (session != null && !ReferenceEquals(session, MyAPIGateway.Session)))
                policy.WorldGone();
            else policy.Update(Now, ReadEnvironment());
            FinishScreen();
        }
        void DetachIfDisposed()
        {
            if (!disposed || screen != null) return;
            MyScreenManager.ScreenAdded -= ScreenAdded;
            MyScreenManager.ScreenRemoved -= ScreenRemoved;
            MyScreenManager.EndOfDraw -= GuardMembership;
        }
        internal void LeaveSession()
        {
            if (!OnGameThread) throw new InvalidOperationException("Pointer world exit requires its game thread.");
            // Called only after the integration owner observes a different/null
            // actual session. The old world's gameplay route no longer exists.
            policy.WorldGone(); RemoveOwnScreen();
            session = null; controlledEntity = 0; capturedCharacter = null; capturedWeapon = null;
            Unregister();
        }
        public void Dispose()
        {
            if (!OnGameThread) throw new InvalidOperationException("Pointer provider disposal requires its game thread.");
            if (disposed) return;
            CheckMembership();
            disposed = true;
            policy.Dispose(Now, ReadEnvironment());
            try { Unregister(); }
            finally
            {
                if (MyAPIGateway.Session == null) policy.WorldGone();
                // The GUI Update/HandleInput methods continue to drain a held-input tail
                // after the plugin stops ticking, then detach this provider themselves.
                FinishScreen();
            }
        }

        sealed class LeaseScreen : MyGuiScreenBase
        {
            readonly PointerProviderNative provider;
            internal readonly long Token;
            internal bool AllowRemoval;
            internal void DisableFocus() { CanHaveFocus = false; }
            internal LeaseScreen(PointerProviderNative provider, long token)
                : base(null, null, null, true, null, 0f, 0f, null)
            {
                this.provider = provider; Token = token;
                CanHaveFocus = true; CanHideOthers = false; CanBeHidden = false;
                EnabledBackgroundFade = false; DrawMouseCursor = true;
                m_isTopMostScreen = true; m_canShareInput = false;
                m_closeOnEsc = false; m_canCloseInCloseAllScreenCalls = false;
                m_drawEvenWithoutFocus = false; SkipTransition = true;
            }
            public override string GetFriendlyName() { return "HDR.Pointer/1.Lease"; }
            public override int GetTransitionOpeningTime() { return 0; }
            public override int GetTransitionClosingTime() { return 0; }
            public override void HandleInput(bool receivedFocusInThisUpdate) { provider.RoutedInput(this); }
            public override void InputLost() { provider.LostInput(this); }
            public override bool Update(bool hasFocus)
            { bool result = base.Update(hasFocus); provider.ScreenTick(this); return result; }
            public override bool Draw() { return true; }
            public override bool CloseScreen(bool isUnloading = false)
            { if (AllowRemoval) return base.CloseScreen(isUnloading); provider.CloseRequested(this); return false; }
            public override void CloseScreenNow(bool isUnloading = false)
            { if (AllowRemoval) base.CloseScreenNow(isUnloading); else provider.CloseRequested(this); }
            public override void OnRemoved() { provider.ScreenRemoved(this); base.OnRemoved(); }
        }
    }
}
