using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public class RawReadService : IRawReadService
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(
        SafeFileHandle hFile,
        long liDistanceToMove,
        out long lpNewFilePointer,
        uint dwMoveMethod);

    public async Task<byte[]> ReadBlockAsync(string physicalPath, long offset, int blockSize, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            using var handle = CreateFile(
                physicalPath,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                throw new IOException(
                    $"Could not open device {physicalPath}. Error: {error}",
                    HResultFromWin32(error));
            }

            if (!SetFilePointerEx(handle, offset, out _, 0)) // 0 = FILE_BEGIN
            {
                var error = Marshal.GetLastWin32Error();
                throw new IOException(
                    $"Could not seek to offset {offset}. Error: {error}",
                    HResultFromWin32(error));
            }

            var buffer = new byte[blockSize];
            if (!ReadFile(handle, buffer, (uint)blockSize, out uint bytesRead, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                throw new IOException(
                    $"Could not read block from {physicalPath}. Error: {error}",
                    HResultFromWin32(error));
            }

            if (bytesRead < blockSize)
            {
                var result = new byte[bytesRead];
                Array.Copy(buffer, result, bytesRead);
                return result;
            }

            return buffer;
        }, cancellationToken);
    }

    private static int HResultFromWin32(int error)
    {
        return error <= 0
            ? error
            : unchecked((int)(0x80070000u | (uint)error));
    }
}
