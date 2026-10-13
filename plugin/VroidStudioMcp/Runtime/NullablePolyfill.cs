// Polyfill for nullable reference metadata the compiler may need to emit for
// switch expressions / tuple types when Nullable is disabled.
namespace System.Runtime.CompilerServices
{
    internal sealed class NullableAttribute : Attribute
    {
        public NullableAttribute(byte value) { }
        public NullableAttribute(byte[] value) { }
    }

    internal sealed class NullableContextAttribute : Attribute
    {
        public NullableContextAttribute(byte value) { }
    }
}
