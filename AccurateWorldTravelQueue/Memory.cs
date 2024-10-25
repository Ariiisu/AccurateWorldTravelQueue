using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AccurateWorldTravelQueue
{
    internal class Memory
    {
        private Process _process;
        private IntPtr _processPtr;
        private List<byte> _textSectionBytes;
        private IntPtr _textSectionStart;

        public Memory(Process process) => UpdateProcess(process);

        public void UpdateProcess(Process process)
        {
            Win32.CloseHandle(_processPtr);

            _process = process;

            _processPtr = Win32.OpenProcess(Win32.ProcessAccessFlags.All, false, process.Id);
            if (!ScanForTextModule())
                throw new Exception("获取 .text 分区失败");

            Patch();
            Win32.CloseHandle(_processPtr);
        }

        private void Patch()
        {
            var checkAddress = ScanText("83 F8 ?? 73 ?? 44 8B C0 1B D2");
            if (checkAddress == IntPtr.Zero)
                throw new Exception("获取 Signature #1 地址失败");

            var addonTextAddress = ScanText("81 C2 F5 ?? ?? ?? E8 ?? ?? ?? ?? 48 8B D0 48 8D 8C 24");
            if (checkAddress == IntPtr.Zero)
                throw new Exception("获取 Signature #2 地址失败");

            var patchCheckBytes = new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 };
            Win32.WriteProcessMemory(_processPtr, checkAddress, patchCheckBytes, (uint)patchCheckBytes.Length, out _);
            var patchAddonBytes = new byte[] { 0xF4, 0x30 };
            Win32.WriteProcessMemory(_processPtr, addonTextAddress + 2, patchAddonBytes, (uint)patchAddonBytes.Length, out _);
        }

        private bool ScanForTextModule()
        {
            var mainModule = _process.MainModule;
            if (mainModule == null)
                throw new Exception("mainModule == null");

            var baseAddress = mainModule.BaseAddress;
            var ntNewOffset = ReadInt32(baseAddress, 0x3C);
            var ntHeader = baseAddress + ntNewOffset;

            // IMAGE_NT_HEADER
            var fileHeader = ntHeader + 4;
            var numSections = ReadInt16(ntHeader, 6);

            // IMAGE_OPTIONAL_HEADER
            var optionalHeader = fileHeader + 20;

            var sectionHeader = optionalHeader + 240;

            var sectionCursor = sectionHeader;
            for (var i = 0; i < numSections; i++)
            {
                var sectionName = ReadInt64(sectionCursor);

                // .text
                switch (sectionName)
                {
                    case 0x747865742E: // .text
                        var offset = ReadInt32(sectionCursor, 12);
                        _textSectionStart = baseAddress + offset;

                        var size = ReadInt32(sectionCursor, 8);

                        // 不知道为什么用 File.ReadAllBytes 的话要减去 0xC00 才能拿到实际地址
                        // 懒得从这里面读 PE header了，就这样吧
                        const int magicOffset = 0xC00;

                        _textSectionBytes = File.ReadAllBytes(mainModule.FileName).ToList().GetRange(offset - magicOffset, size);
                        return true;
                }

                sectionCursor += 40;
            }

            return false;
        }

        private byte[] ReadBytes(IntPtr offset, uint length)
        {
            var bytes = new byte[length];
            Win32.ReadProcessMemory(_processPtr,
                offset, bytes, new IntPtr(length), IntPtr.Zero);
            return bytes;
        }

        public long ReadInt64(IntPtr address, int offset = 0) => BitConverter.ToInt64(ReadBytes(IntPtr.Add(address, offset), 8), 0);
        public short ReadInt16(IntPtr address, int offset = 0) => BitConverter.ToInt16(ReadBytes(IntPtr.Add(address, offset), 2), 0);
        private int ReadInt32(IntPtr address, int offset = 0) => BitConverter.ToInt32(ReadBytes(IntPtr.Add(address, offset), 4), 0);

        private static ushort[] ParseSignature(string signature)
        {
            var bytesStr = signature.Split(' ');
            var bytes = new ushort[bytesStr.Length];

            for (var i = 0; i < bytes.Length; i++)
            {
                var str = bytesStr[i];
                if (str.Contains('?'))
                {
                    bytes[i] = 0xFFFF;
                    continue;
                }

                bytes[i] = Convert.ToByte(str, 16);
            }

            return bytes;
        }

        private IntPtr ScanText(string signature)
        {
            var bytes = ParseSignature(signature);

            var firstByte = bytes[0];

            var scanRet = IntPtr.Zero;

            var scanSize = _textSectionBytes.Count - bytes.Length;
            for (var i = 0; i < scanSize; i++)
            {
                if (firstByte != 0xFFFF && (i = _textSectionBytes.IndexOf((byte)firstByte, i)) == -1) break;

                var found = true;

                for (var j = 1; j < bytes.Length; j++)
                {
                    var isWildCard = bytes[j] == 0xFFFF;
                    var isEqual = bytes[j] == _textSectionBytes[j + i];

                    if (isWildCard || isEqual) continue;
                    found = false;
                    break;
                }

                if (!found)
                    continue;
                scanRet = _textSectionStart + i;
                break;
            }

            return scanRet;
        }
    }
}