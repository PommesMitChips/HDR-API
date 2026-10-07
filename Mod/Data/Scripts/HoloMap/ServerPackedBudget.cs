using System;
using System.Collections.Generic;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        // Bound initial validation, including failed attempts. Admission and
        // commit share decoded data rather than decompressing it twice.
        readonly Dictionary<string,PackedAnimation> _serverPackedMemo = new Dictionary<string,PackedAnimation>();
        int _serverDecodeTick = -1, _serverDecodeTickCount, _serverDecodeWindow = -60, _serverDecodeWindowCount;
        PackedAnimation DecodeServerPacked(string source)
        {
            if (string.IsNullOrEmpty(source) || source.Length > PackedAnimation.MaxEncodedCharacters)
                throw new ArgumentException("Packed animation source exceeds its character budget.");
            PackedAnimation decoded;
            foreach (var state in _packed.Values) if (state.Source == source) return state.Animation;
            if (_serverPackedMemo.TryGetValue(source, out decoded))
            {
                if (decoded == null) throw new ArgumentException("Invalid packed animation source.");
                return decoded;
            }
            if (_serverDecodeTick != _ticks) { _serverDecodeTick = _ticks; _serverDecodeTickCount = 0; }
            if (_ticks - _serverDecodeWindow >= 60 || _ticks < _serverDecodeWindow)
            { _serverDecodeWindow = _ticks; _serverDecodeWindowCount = 0; }
            if (_serverDecodeTickCount >= 2 || _serverDecodeWindowCount >= 8)
                throw new ArgumentException("Packed animation validation is busy; retry on a later PB update.");
            _serverDecodeTickCount++; _serverDecodeWindowCount++;
            if (_serverPackedMemo.Count >= 2) _serverPackedMemo.Clear();
            try
            {
                decoded = PackedAnimation.Decode(source);
                _serverPackedMemo.Add(source, decoded);
                return decoded;
            }
            catch (ArgumentException) { _serverPackedMemo[source] = null; throw; }
        }
        void ClearServerPackedBudget()
        {
            _serverPackedMemo.Clear(); _serverDecodeTick = -1; _serverDecodeTickCount = 0;
            _serverDecodeWindow = -60; _serverDecodeWindowCount = 0;
        }
    }
}
