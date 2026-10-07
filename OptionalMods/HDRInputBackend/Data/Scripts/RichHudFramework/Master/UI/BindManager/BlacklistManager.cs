using ProtoBuf;
using RichHudFramework.Internal;
using RichHudFramework.UI.Server;
using Sandbox.Game;
using System.Collections.Generic;
using System;

namespace RichHudFramework.Server
{
    internal class BlacklistManager : ModBase.ModuleBase
    {
        private static BlacklistManager instance;

        public BlacklistManager() : base(true, false, RichHudMaster.Instance)
        { }

        public static void Init()
        {
            if (instance == null && ExceptionHandler.IsServer)
            {
                instance = new BlacklistManager();
            }
        }

        /// <summary>
        /// Release resources
        /// </summary>
        public override void Close()
        {
            instance = null;
        }

        public static void SetBlacklist(long identID, IReadOnlyList<BindRange> blacklist, bool value)
        {
            if (identID > 0 && instance != null && ValidateRanges(blacklist))
                instance.SetBlacklistInternal(identID, blacklist, value);
        }

        internal static bool ValidateRanges(IReadOnlyList<BindRange> ranges)
        {
            int count = BindManager.BuiltInBinds.Length;
            if (ranges == null || ranges.Count > count) return false;
            int total = 0;
            for (int i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (range.Start < 0 || range.Start > count || range.Count < 0 || range.Count > count - range.Start)
                    return false;
                total += range.Count;
                if (total > count) return false;
            }
            return true;
        }

        public static void SetBlacklist(long identID, IReadOnlyList<string> blacklist, bool value)
        {
            if (identID > 0 && blacklist != null && blacklist.Count <= BindManager.BuiltInBinds.Length)
            {
                foreach (string control in blacklist)
                {
                    if (control == null || control.Length > 80 || Array.IndexOf(BindManager.BuiltInBinds, control) < 0)
                        return;
                }
                foreach (string control in blacklist)
                    MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(control, identID, !value);
            }
        }

        private void SetBlacklistInternal(long identID, IReadOnlyList<BindRange> ranges, bool value)
        {
            if (!ValidateRanges(ranges)) return;

            for (int n = 0; n < ranges.Count; n++)
            {
                BindRange range = ranges[n];
                int end = range.Start + range.Count;

                for (int i = range.Start; i < end; i++)
                {
                    if (i >= 0 && i < BindManager.BuiltInBinds.Length)
                    {
                        string controlName = BindManager.BuiltInBinds[i];
                        MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(controlName, identID, !value);
                    }
                }
            }
        }
    }

    [ProtoContract]
    internal struct BindRange
    {
        [ProtoMember(1)]
        public int Start; // Starting index in BuiltInBinds

        [ProtoMember(2)]
        public int Count; // Number of contiguous elements

        public BindRange(int start, int count)
        {
            Start = start;
            Count = count;
        }
    }

    [ProtoContract]
    internal struct BlacklistMessage
    {
        [ProtoMember(1)]
        public BindRange[] ranges;

        [ProtoMember(2)]
        public bool value;

        public BlacklistMessage(BindRange[] ranges, bool value)
        {
            this.ranges = ranges;
            this.value = value;
        }
    }

    [ProtoContract]
    internal struct BlacklistMessageOld
    {
        [ProtoMember(1)]
        public string[] controls;

        [ProtoMember(2)]
        public bool value;

        public BlacklistMessageOld(string[] controls, bool value)
        {
            this.controls = controls;
            this.value = value;
        }
    }
}
