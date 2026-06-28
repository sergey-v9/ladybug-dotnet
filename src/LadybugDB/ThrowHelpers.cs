using System;

namespace LadybugDB;

internal static class ThrowHelpers
{
    internal static T ThrowIfNull<T>(T? argument, string paramName)
        where T : class
    {
#if NET7_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(argument, paramName);
        return argument;
#else
        if (argument is null)
        {
            throw new ArgumentNullException(paramName);
        }

        return argument;
#endif
    }

    internal static void ThrowIfDisposed(bool condition, object instance)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(condition, instance);
#else
        if (condition)
        {
            throw new ObjectDisposedException(instance.GetType().Name);
        }
#endif
    }
}
