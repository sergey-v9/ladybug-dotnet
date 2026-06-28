using System;
using System.Runtime.InteropServices;
using LadybugDB.Interop;

namespace LadybugDB;

/// <summary>
/// Owns an unmanaged Arrow C-Data-Interface <c>ArrowSchema</c> block. The native engine fills the
/// block via <see cref="QueryResult.GetArrowSchema"/>; this wrapper invokes the Arrow
/// <c>release</c> callback (when present) and frees the block on disposal. IntPtr-backed so the core
/// assembly takes no managed-Arrow dependency — the friendly Apache.Arrow layer lives in the
/// separate <c>LadybugDB.Arrow</c> package.
/// </summary>
public sealed class ArrowSchemaHandle : IDisposable
{
    // Layout/size/offsets come from the ABI-exact blittable mirror in Interop (NativeTypes.cs), so
    // they track the C struct on every architecture without hand-computed offsets.
    private static readonly int Size = Marshal.SizeOf<ArrowSchema>();
    private static readonly int ReleaseOffset = (int)Marshal.OffsetOf<ArrowSchema>(nameof(ArrowSchema.Release));

    private IntPtr _ptr;
    private int _disposed;

    private ArrowSchemaHandle(IntPtr ptr) => _ptr = ptr;

    /// <summary>Allocates a zero-initialized unmanaged ArrowSchema block for the producer to fill.</summary>
    public static ArrowSchemaHandle Allocate()
    {
        IntPtr ptr = Marshal.AllocHGlobal(Size);
        Zero(ptr, Size);
        return new ArrowSchemaHandle(ptr);
    }

    /// <summary>The pointer to the unmanaged ArrowSchema block. Throws once disposed.</summary>
    public IntPtr Ptr
    {
        get
        {
            ThrowHelpers.ThrowIfDisposed(System.Threading.Volatile.Read(ref _disposed) != 0, this);
            return _ptr;
        }
    }

    /// <summary>
    /// True when the block holds no live Arrow struct (null <c>release</c> pointer), e.g. directly
    /// after allocation or after the contents were moved to a consumer.
    /// </summary>
    public bool IsReleased => Marshal.ReadIntPtr(Ptr, ReleaseOffset) == IntPtr.Zero;

    /// <summary>
    /// Frees only the unmanaged block WITHOUT invoking the Arrow release callback. Call this after a
    /// consumer (e.g. Apache.Arrow's <c>CArrowSchemaImporter</c>) has taken ownership of the struct
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
    private delegate void ReleaseFn(IntPtr schema);
#endif
}
