using System;
using System.Runtime.InteropServices;
using LadybugDB.Interop;

namespace LadybugDB;

/// <summary>
/// Owns an unmanaged Arrow C-Data-Interface <c>ArrowArray</c> block (one chunk of a query result).
/// Mirrors <see cref="ArrowSchemaHandle"/>: the engine fills it via
/// <see cref="QueryResult.GetNextArrowChunk"/>, and this wrapper invokes the Arrow <c>release</c>
/// callback (when present) and frees the block on disposal.
/// </summary>
public sealed class ArrowArrayHandle : IDisposable
{
    // Layout/size/offsets come from the ABI-exact blittable mirror in Interop (NativeTypes.cs).
    private static readonly int Size = Marshal.SizeOf<ArrowArray>();
    private static readonly int ReleaseOffset = (int)Marshal.OffsetOf<ArrowArray>(nameof(ArrowArray.Release));
    private static readonly int LengthOffset = (int)Marshal.OffsetOf<ArrowArray>(nameof(ArrowArray.Length));

    private IntPtr _ptr;
    private int _disposed;

    private ArrowArrayHandle(IntPtr ptr) => _ptr = ptr;

    /// <summary>Allocates a zero-initialized unmanaged ArrowArray block for the producer to fill.</summary>
    public static ArrowArrayHandle Allocate()
    {
        IntPtr ptr = Marshal.AllocHGlobal(Size);
        Zero(ptr, Size);
        return new ArrowArrayHandle(ptr);
    }

    /// <summary>The pointer to the unmanaged ArrowArray block. Throws once disposed.</summary>
    public IntPtr Ptr
    {
        get
        {
            if (System.Threading.Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ArrowArrayHandle));
            }

            return _ptr;
        }
    }

    /// <summary>
    /// True when the engine reported an empty chunk (null <c>release</c> pointer): end of stream.
    /// </summary>
    public bool IsReleased => Marshal.ReadIntPtr(Ptr, ReleaseOffset) == IntPtr.Zero;

    /// <summary>The chunk row count (<c>ArrowArray.length</c>).</summary>
    public long Length => Marshal.ReadInt64(Ptr, LengthOffset);

    /// <summary>
    /// Frees only the unmanaged block WITHOUT invoking the Arrow release callback. Call this after a
    /// consumer (e.g. Apache.Arrow's <c>CArrowArrayImporter</c>) has taken ownership of the struct
    /// contents and already released them, so disposal must not release a second time.
    /// </summary>
    public void DetachAndFree()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        IntPtr ptr = _ptr;
        _ptr = IntPtr.Zero;
        if (ptr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        IntPtr ptr = _ptr;
        _ptr = IntPtr.Zero;
        if (ptr == IntPtr.Zero)
        {
            return;
        }

        IntPtr release = Marshal.ReadIntPtr(ptr, ReleaseOffset);
        if (release != IntPtr.Zero)
        {
            InvokeRelease(release, ptr);
        }

        Marshal.FreeHGlobal(ptr);
    }

    private static void Zero(IntPtr ptr, int size)
    {
        for (int i = 0; i < size; i++)
        {
            Marshal.WriteByte(ptr, i, 0);
        }
    }

    private static unsafe void InvokeRelease(IntPtr release, IntPtr target)
    {
#if NET7_0_OR_GREATER
        ((delegate* unmanaged[Cdecl]<IntPtr, void>)release)(target);
#else
        Marshal.GetDelegateForFunctionPointer<ReleaseFn>(release)(target);
#endif
    }

#if !NET7_0_OR_GREATER
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseFn(IntPtr array);
#endif
}
