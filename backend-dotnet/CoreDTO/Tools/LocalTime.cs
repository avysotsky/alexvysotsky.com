using System;

namespace CoreDTO.Tools
{
    public delegate DateTime LocalDateTime();
    public static class LocalTime
    {
        public static LocalDateTime GetLocalTimeFunc(int gmtOffset)
        {
            return () => DateTime.UtcNow.AddHours(gmtOffset);
        }
    }
}
