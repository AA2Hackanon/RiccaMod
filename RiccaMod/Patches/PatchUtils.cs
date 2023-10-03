using MelonLoader.NativeUtils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RiccaMod.Patches
{
    internal static class PatchUtils
    {


        public enum AllocationProtectEnum : uint
        {
            PAGE_EXECUTE = 0x00000010,
            PAGE_EXECUTE_READ = 0x00000020,
            PAGE_EXECUTE_READWRITE = 0x00000040,
            PAGE_EXECUTE_WRITECOPY = 0x00000080,
            PAGE_NOACCESS = 0x00000001,
            PAGE_READONLY = 0x00000002,
            PAGE_READWRITE = 0x00000004,
            PAGE_WRITECOPY = 0x00000008,
            PAGE_GUARD = 0x00000100,
            PAGE_NOCACHE = 0x00000200,
            PAGE_WRITECOMBINE = 0x00000400
        }
        public enum StateEnum : uint
        {
            MEM_COMMIT = 0x1000,
            MEM_FREE = 0x10000,
            MEM_RESERVE = 0x2000
        }

        public enum TypeEnum : uint
        {
            MEM_IMAGE = 0x1000000,
            MEM_MAPPED = 0x40000,
            MEM_PRIVATE = 0x20000
        }
        [StructLayout(LayoutKind.Sequential)]

        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public AllocationProtectEnum AllocationProtect;
            public IntPtr RegionSize;
            public StateEnum State;
            public AllocationProtectEnum Protect;
            public TypeEnum Type;
        }
        [Flags]
        public enum AllocationType
        {
            Commit = 0x1000,
            Reserve = 0x2000,
            Decommit = 0x4000,
            Release = 0x8000,
            Reset = 0x80000,
            Physical = 0x400000,
            TopDown = 0x100000,
            WriteWatch = 0x200000,
            LargePages = 0x20000000
        }

        [Flags]
        public enum MemoryProtection
        {
            Execute = 0x10,
            ExecuteRead = 0x20,
            ExecuteReadWrite = 0x40,
            ExecuteWriteCopy = 0x80,
            NoAccess = 0x01,
            ReadOnly = 0x02,
            ReadWrite = 0x04,
            WriteCopy = 0x08,
            GuardModifierflag = 0x100,
            NoCacheModifierflag = 0x200,
            WriteCombineModifierflag = 0x400
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);
        [DllImport("kernel32", SetLastError = true)]
        public static extern IntPtr VirtualAlloc(IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int VirtualQuery(
            UIntPtr lpAddress,
            ref MEMORY_BASIC_INFORMATION lpBuffer,
            int dwLength
        );


        /// <summary>
        /// Patches a place in code by replacing bytes with given, new bytes.
        /// Does so by adding write rights to the page, then reverting the rights after its done.
        /// Also performs a verify before writing, to make sure the replaced code is actually intended and did
        /// not change unexpectedly, throwing an exception if it fails.
        /// </summary>
        /// <param name="address">Address to start patching</param>
        /// <param name="newBytes">new bytes to write</param>
        /// <param name="verify">a (potentially empty) list of bytes that need to be at the target location before the patch</param>
        /// <param name="replacedBytes">the bytes that were replaced</param>
        /// <exception cref="InvalidOperationException">Thrown if verify fails</exception>
        public unsafe static void ReplaceCodeBytes(ulong address, byte[] newBytes, byte[] verify, out byte[] replacedBytes)
        {

            IntPtr proc = Process.GetCurrentProcess().Handle;
            IntPtr area = (IntPtr)address;
            bool suc = VirtualProtectEx(proc, area, (UIntPtr)newBytes.Length, (uint)AllocationProtectEnum.PAGE_EXECUTE_READWRITE, out uint oldRights);
            if (!suc)
            {
                int err = Marshal.GetLastWin32Error();
                var exc = new Win32Exception(err);
                throw new InvalidOperationException($"Failed to get write rights to Page at {address:X}: Error {err} \"{exc.Message}\"");
            }

            try
            {
                byte* it = (byte*)area;
                for (int i = 0; i < verify.Length; i++)
                {
                    if (it[i] != verify[i])
                    {
                        byte[] actual = new byte[verify.Length];
                        for (i = 0; i < verify.Length; i++) actual[i] = it[i];
                        throw new InvalidOperationException("Code Injection Verification failed: Expected " + String.Join(" ", verify.Select(x => $"[{x:X}"))
                            + ", got " + String.Join(" ", actual.Select(x => $"[{x:X}")));
                    }
                }
                replacedBytes = new byte[newBytes.Length];
                for (int i = 0; i < newBytes.Length; i++)
                {
                    replacedBytes[i] = it[i];
                    it[i] = newBytes[i];
                }
            }
            finally
            {
                VirtualProtectEx(proc, area, (UIntPtr)newBytes.Length, oldRights, out oldRights);
            }
        }

        /// <summary>
        /// Crawls through the Address space and uses VirtualAlloc to allocate a set of pages that are 
        /// at maximum a given distance away from a certain location, so that relative assembly instructions
        /// can reach this memory.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="size"></param>
        /// <param name="maxDist"></param>
        /// <param name="rights"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public unsafe static IntPtr AllocateCloseTo(ulong address, int size, ulong maxDist, MemoryProtection rights)
        {
            ulong pageSize = (ulong)System.Environment.SystemPageSize;
            address = address / pageSize * pageSize;

            MEMORY_BASIC_INFORMATION infoStruct = new MEMORY_BASIC_INFORMATION();
            ulong addressToAllocate = 0;

            for (ulong offset = 0; offset < maxDist; offset += pageSize)
            {
                ulong addr = address + offset;
                int ret = VirtualQuery((UIntPtr)addr, ref infoStruct, sizeof(MEMORY_BASIC_INFORMATION));
                if (ret != 0 && infoStruct.State == StateEnum.MEM_FREE)
                {
                    addressToAllocate = addr;
                    IntPtr alloced = VirtualAlloc((IntPtr)addressToAllocate, (uint)pageSize, (uint)(AllocationType.Commit | AllocationType.Reserve), (uint)rights);
                    if (alloced != (IntPtr)0)
                    {
                        return (IntPtr)alloced;
                    }

                    int err = Marshal.GetLastWin32Error();
                    var exc = new Win32Exception(err);
                    //damn, failed, keep trying
                }
                else if (ret == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    var exc = new Win32Exception(err);
                }
            }
            throw new InvalidOperationException($"Failed to find a free page around {address:X} in a distance of {maxDist:X}");
        }

        public class StubFunctionData
        {
            public class RelativeRedirect
            {
                public uint AddressOffset;
                public ulong TargetAddress;
            }
            public ulong FunctionOffset { get; init; }
            public uint InjectionOffset { get; init; }
            public uint InjectionReplacementSize { get; init; }
            public byte[] Assembly { get; init; }
            public uint AssemblyHookFunctionOffset { get; init; }
            public uint AssemblyReturnJumpOffset { get; init; }
            public RelativeRedirect[] Redirects { get; init; }
            public byte[] ReplacedBytesToVerify { get; init; }

        }
        public class StubFunction
        {
            public byte[] OrigData { get; init; }
            public IntPtr StubStart { get; init; }
        }

        public class StubInjection<T> where T : Delegate
        {
            public StubFunctionData Data { get; init; }
            public StubFunction Function { get; init; }
            public NativeHook<T> Hook { get; init; }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr User32SetTimerDelegate(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);
        private static NativeHook<User32SetTimerDelegate> user32Hook = new();
        static byte[] origs = new byte[0];

        private class StubMemory
        {
            public IntPtr memory = IntPtr.Zero;
            public uint freeOffset = 0;
        }
        private static StubMemory stubMem = new();
        private static IntPtr stubMemory = IntPtr.Zero;
        public static StubFunction DoStubInjection(StubFunctionData data, Environment env)
        {
            byte[] orig = new byte[0];

            ulong injectionPoint = env.GameDllBase + data.FunctionOffset + data.InjectionOffset;
            //alloc some memory if not there allready
            if (stubMem.memory == IntPtr.Zero)
            {
                stubMem.memory = PatchUtils.AllocateCloseTo(injectionPoint, 2048, 0xEFFFFFFFF, PatchUtils.MemoryProtection.ExecuteReadWrite);
            }
            ulong stubAddress = (ulong)stubMem.memory + stubMem.freeOffset;

            //1: write assembly code to stub address
            int paddedAssemblyLength = (int)(data.Assembly.Length - 1) / 32 * 32 + 32;
            unsafe
            {
                byte* it = (byte*)stubAddress;
                int i;
                for (i = 0; i < data.Assembly.Length; i++)
                {
                    it[i] = data.Assembly[i];
                }
                for (; i < paddedAssemblyLength; i++)
                {
                    it[i] = 0xCC; //padd with debug traps
                }
            }
            stubMem.freeOffset += (uint)paddedAssemblyLength;
            //2: patch exit jump to continue in original function
            unsafe
            {
                ulong jmpAddrLoc = stubAddress + data.AssemblyReturnJumpOffset + 1;
                ulong jmpEndLoc = stubAddress + data.AssemblyReturnJumpOffset + 5;
                ulong jmpTargetLoc = injectionPoint + data.InjectionReplacementSize;
                ulong addrDif = jmpTargetLoc - jmpEndLoc;
                byte* it = (byte*)jmpAddrLoc;
                it[0] = (byte)(addrDif >> 0);
                it[1] = (byte)(addrDif >> 8);
                it[2] = (byte)(addrDif >> 16);
                it[3] = (byte)(addrDif >> 24);
            }

            //2.2: patch additoinal redirects if requested
            foreach (StubFunctionData.RelativeRedirect r in data.Redirects)
            {
                unsafe
                {
                    ulong addrValueLoc = stubAddress + r.AddressOffset;
                    ulong addrDif = r.TargetAddress - (addrValueLoc + 4);
                    byte* it = (byte*)addrValueLoc;
                    it[0] = (byte)(addrDif >> 0);
                    it[1] = (byte)(addrDif >> 8);
                    it[2] = (byte)(addrDif >> 16);
                    it[3] = (byte)(addrDif >> 24);
                }
            }

            //3: patch in jump to patched function
            if (data.InjectionReplacementSize < 5) throw new ArgumentException("Replacement needs at least 5 byte for a jump instruction", "data");
            ulong addrDiff = stubAddress - (injectionPoint + 5);
            byte[] injectedCode = new byte[data.InjectionReplacementSize];
            //put jump instructoin (E9) with replaive 4 byte offset to target
            Array.Copy(new byte[] { 0xE9, (byte)(addrDiff >> 0), (byte)(addrDiff >> 8), (byte)(addrDiff >> 16), (byte)(addrDiff >> 24) }, injectedCode, 5);
            //fill rest with nops
            for (int i = 5; i < data.InjectionReplacementSize; i++)
            {
                injectedCode[i] = 0x90;
            }
            //write code
            PatchUtils.ReplaceCodeBytes(
                injectionPoint
                , injectedCode
                , data.ReplacedBytesToVerify
                , out orig
             );

            return new StubFunction { OrigData = orig, StubStart = (IntPtr)(stubAddress + data.AssemblyHookFunctionOffset) };
        }
    }
}
