using System.IO;

namespace TouchMirror.AirPlay;

public sealed class FairPlay
{
    private static readonly byte[][] Replies =
    {
        Convert.FromHexString(
            "46504c59030102000000008202000f9f3f9e0a2521dbdf312ab2bfb29e8d232b" +
            "6376a8c818701d22ae93d82737feaf9db4fdf41c2dba9d1f49caaabf6591ac1f" +
            "7bc6f7e0663d21afe01565953eab81f418ceed095adb7c3d0e254909a79831d4" +
            "9c3982973434facb42c63a1cd911a6fe941a8a6d4a743b46c3a7649e44c78955" +
            "e49d8155009549c4e2f7a3f6d5ba"),
        Convert.FromHexString(
            "46504c5903010200000000820201cf32a25714b2524f8aa0ad7af164e37bcf44" +
            "24e200047efc0ad67afcd95ded1c2730bb591b962ed63a9c4ded88ba8fc78de6" +
            "4d91ccfd5c7b56da88e31f5cceafc7431995a01665a54e1939d25b94db64b9e4" +
            "5d8d063e1e6af07e9656162b0efa404275ea5a44d9591c7256b9fbe6513898b8" +
            "0227721988571650942ad946688a"),
        Convert.FromHexString(
            "46504c5903010200000000820202c169a352eeed35b18cdd9c58d64f16c1519a" +
            "89eb5317bd0d4336cd68f638ff9d016a5b52b7fa9216b2b65482c78444118121" +
            "a2c7fed83db7119e9182aad7d18c7063e2a457555910af9e0efc76347d164043" +
            "807f581ee4fbe42ca9dedc1b5eb2a3aa3d2ecd59e7eee70b3629f22afd161d87" +
            "7353ddb99adc8e07006e56f850ce"),
        Convert.FromHexString(
            "46504c59030102000000008202039001e1727e0f57f9f5880db104a6257a23f5" +
            "cfff1abbe1e93045251afb97eb9fc0011ebe0f3a81df5b691d76acb2f7a5c708" +
            "e3d328f56bb39dbde5f29c8a17f481487e3ae863c678325422e6f78e166d18aa" +
            "7fd636258bce28726f661f738893ce44311e4be6c0535193e5ef72e868623372" +
            "9c227d820c999445d89246c8c359"),
    };

    private static readonly byte[] FpHeaderV3 = Convert.FromHexString("46504c590301040000000014");

    private byte[]? _keyMsg;

    public byte[]? KeyMsg => _keyMsg;

    public byte[]? HandleSetup(byte[] request)
    {
        if (request.Length == 16)
        {
            if (request[4] != 0x03)
                return null;
            _keyMsg = null;
            return Replies[request[14] & 3];
        }

        if (request.Length == 164)
        {
            if (request[4] != 0x03)
                return null;
            _keyMsg = request;
            var res = new byte[32];
            Array.Copy(FpHeaderV3, res, 12);
            Array.Copy(request, 144, res, 12, 20);
            return res;
        }

        return null;
    }
}
