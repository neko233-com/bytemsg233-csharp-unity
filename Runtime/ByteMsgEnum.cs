using System;
using System.Globalization;

namespace ByteMsg233
{
    public static class ByteMsgEnum
    {
        public static TEnum FromValue<TEnum>(int value) where TEnum : struct, Enum
        {
            if (!IsDefinedValue<TEnum>(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"Unknown {typeof(TEnum).Name} value.");
            }

            return (TEnum)Enum.ToObject(typeof(TEnum), value);
        }

        public static bool IsDefinedValue<TEnum>(int value) where TEnum : struct, Enum
        {
            try
            {
                var underlying = Convert.ChangeType(value, Enum.GetUnderlyingType(typeof(TEnum)), CultureInfo.InvariantCulture);
                return Enum.IsDefined(typeof(TEnum), underlying);
            }
            catch (OverflowException) { return false; }
        }

        public static int ToValue<TEnum>(TEnum value) where TEnum : struct, Enum
        {
            return Convert.ToInt32(value);
        }
    }
}
