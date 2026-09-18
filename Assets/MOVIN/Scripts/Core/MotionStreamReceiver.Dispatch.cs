using UnityEngine;
using MOVIN.OSC;

namespace MOVIN
{
    public partial class MotionStreamReceiver
    {
        public const string DefaultStreamTarget = "Unity";
        private const string AddressNamespace = "/MOVIN";

        /// <summary>Address MOVIN Studio sends the root bone pose on for <paramref name="target"/>.</summary>
        public static string RootAddressFor(string target) => $"{AddressNamespace}/{NormalizeTarget(target)}/Root";

        /// <summary>Address MOVIN Studio sends every other bone pose on for <paramref name="target"/>.</summary>
        public static string BoneAddressFor(string target) => $"{AddressNamespace}/{NormalizeTarget(target)}/Bone";

        private static string NormalizeTarget(string target)
        {
            var trimmed = target?.Trim().Trim('/');
            return string.IsNullOrEmpty(trimmed) ? DefaultStreamTarget : trimmed;
        }

        private void ResolveStreamAddresses()
        {
            _rootAddress = RootAddressFor(streamTarget);
            _boneAddress = BoneAddressFor(streamTarget);
        }

        // StartReceiver resolves the addresses before the receive thread exists. This covers
        // callers that never open the socket, such as edit mode tests.
        private void EnsureStreamAddresses()
        {
            if (_rootAddress == null || _boneAddress == null)
                ResolveStreamAddresses();
        }

        private bool IsRootAddress(string address) => address == _rootAddress;

        private bool IsBoneAddress(string address) => address == _boneAddress;

        private bool IsMotionAddress(string address) => IsRootAddress(address) || IsBoneAddress(address);

        /// <summary>
        /// Main-thread handling of the messages the receive thread did not consume: the stream
        /// validation end control message, which drains buffered frames onto the character, and
        /// anything else, which is ignored.
        /// </summary>
        private void DispatchMainThreadMessage(OSCMessage msg)
        {
            if (verboseLogging)
                Debug.Log($"OSC {msg.Address} {msg.Types} [{string.Join(", ", msg.Args)}]");

            if (msg.Address == ValidationEndAddress)
                EndValidationSession(msg);
        }

        private static bool TryReadFrameIndex(OSCMessage msg, out int frameIdx, out int argOffset)
        {
            if (msg.Args.Length > 0)
            {
                if (msg.Args[0] is int i)
                {
                    frameIdx = i;
                    argOffset = 1;
                    return true;
                }

                if (msg.Args[0] is float f)
                {
                    frameIdx = Mathf.RoundToInt(f);
                    if (Mathf.Approximately(f, frameIdx))
                    {
                        argOffset = 1;
                        return true;
                    }
                }
            }

            frameIdx = 0;
            argOffset = 0;
            return false;
        }

        /// <summary>
        /// Converts a wire frame index to a logical frame number. MOVIN encodes validation
        /// packets as negative indices (-1 => frame 0, -2 => frame 1, ...); non-negative
        /// indices pass through unchanged.
        /// </summary>
        private static int WireFrameToFrame(int wireFrame)
        {
            return wireFrame < 0 ? -wireFrame - 1 : wireFrame;
        }
    }
}
