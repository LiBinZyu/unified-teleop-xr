using System;
using System.Runtime.InteropServices;

namespace CycloneDDS
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DdsTime
    {
        public int sec;
        public uint nanosec;

        public DdsTime(int sec, uint nanosec)
        {
            this.sec = sec;
            this.nanosec = nanosec;
        }

        public static DdsTime Now()
        {
            long ticks = DateTime.UtcNow.Ticks - 621355968000000000L;
            return new DdsTime((int)(ticks / 10000000L), (uint)((ticks % 10000000L) * 100L));
        }

        public long ToNanoseconds()
        {
            return (long)sec * 1000000000L + (long)nanosec;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DdsHeader
    {
        public DdsTime stamp;
        [MarshalAs(UnmanagedType.LPStr)]
        public string frame_id;

        public DdsHeader(string frameId)
        {
            stamp = DdsTime.Now();
            frame_id = frameId ?? string.Empty;
        }

        public DdsHeader(DdsTime time, string frameId)
        {
            stamp = time;
            frame_id = frameId ?? string.Empty;
        }
    }

    // ==========================================
    // std_msgs structs
    // ==========================================
    [StructLayout(LayoutKind.Sequential)]
    public struct StdBool { public bool data; public StdBool(bool val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdByte { public byte data; public StdByte(byte val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdChar { public byte data; public StdChar(byte val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdColorRGBA { public float r, g, b, a; public StdColorRGBA(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdEmpty { public byte dummy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdFloat32 { public float data; public StdFloat32(float val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdFloat64 { public double data; public StdFloat64(double val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdInt8 { public sbyte data; public StdInt8(sbyte val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdInt16 { public short data; public StdInt16(short val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdInt32 { public int data; public StdInt32(int val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdInt64 { public long data; public StdInt64(long val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdUInt8 { public byte data; public StdUInt8(byte val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdUInt16 { public ushort data; public StdUInt16(ushort val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdUInt32 { public uint data; public StdUInt32(uint val) { data = val; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct StdUInt64 { public ulong data; public StdUInt64(ulong val) { data = val; } }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct StdString
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string data;
        public StdString(string val) { data = val ?? string.Empty; }
    }

    // ==========================================
    // geometry_msgs structs
    // ==========================================
    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryPoint { public double x, y, z; public GeometryPoint(double x, double y, double z) { this.x = x; this.y = y; this.z = z; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryPoint32 { public float x, y, z; public GeometryPoint32(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryVector3 { public double x, y, z; public GeometryVector3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryQuaternion { public double x, y, z, w; public GeometryQuaternion(double x, double y, double z, double w) { this.x = x; this.y = y; this.z = z; this.w = w; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryPose
    {
        public GeometryPoint position;
        public GeometryQuaternion orientation;
        public GeometryPose(GeometryPoint pos, GeometryQuaternion rot) { position = pos; orientation = rot; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryPose2D { public double x, y, theta; public GeometryPose2D(double x, double y, double th) { this.x = x; this.y = y; theta = th; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryTransform
    {
        public GeometryVector3 translation;
        public GeometryQuaternion rotation;
        public GeometryTransform(GeometryVector3 trans, GeometryQuaternion rot) { translation = trans; rotation = rot; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryTwist
    {
        public GeometryVector3 linear;
        public GeometryVector3 angular;
        public GeometryTwist(GeometryVector3 lin, GeometryVector3 ang) { linear = lin; angular = ang; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryAccel
    {
        public GeometryVector3 linear;
        public GeometryVector3 angular;
        public GeometryAccel(GeometryVector3 lin, GeometryVector3 ang) { linear = lin; angular = ang; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryWrench
    {
        public GeometryVector3 force;
        public GeometryVector3 torque;
        public GeometryWrench(GeometryVector3 f, GeometryVector3 t) { force = f; torque = t; }
    }

    // Stamped Variants
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct GeometryPointStamped
    {
        public DdsHeader header;
        public GeometryPoint point;
        public GeometryPointStamped(DdsHeader hdr, GeometryPoint pt) { header = hdr; point = pt; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct GeometryVector3Stamped
    {
        public DdsHeader header;
        public GeometryVector3 vector;
        public GeometryVector3Stamped(DdsHeader hdr, GeometryVector3 vec) { header = hdr; vector = vec; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct GeometryQuaternionStamped
    {
        public DdsHeader header;
        public GeometryQuaternion quaternion;
        public GeometryQuaternionStamped(DdsHeader hdr, GeometryQuaternion q) { header = hdr; quaternion = q; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct GeometryPoseStamped
    {
        public DdsHeader header;
        public GeometryPose pose;
        public GeometryPoseStamped(DdsHeader hdr, GeometryPose p) { header = hdr; pose = p; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct GeometryTransformStamped
    {
        public DdsHeader header;
        [MarshalAs(UnmanagedType.LPStr)]
        public string child_frame_id;
        public GeometryTransform transform;
        public GeometryTransformStamped(DdsHeader hdr, string childFrame, GeometryTransform tf)
        {
            header = hdr;
            child_frame_id = childFrame ?? string.Empty;
            transform = tf;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct GeometryTwistStamped
    {
        public DdsHeader header;
        public GeometryTwist twist;
        public GeometryTwistStamped(DdsHeader hdr, GeometryTwist tw) { header = hdr; twist = tw; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct GeometryAccelStamped
    {
        public DdsHeader header;
        public GeometryAccel accel;
        public GeometryAccelStamped(DdsHeader hdr, GeometryAccel ac) { header = hdr; accel = ac; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct GeometryWrenchStamped
    {
        public DdsHeader header;
        public GeometryWrench wrench;
        public GeometryWrenchStamped(DdsHeader hdr, GeometryWrench w) { header = hdr; wrench = w; }
    }
}
