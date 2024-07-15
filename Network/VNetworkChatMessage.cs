using System.IO;

namespace Bloodstone.Network;

public interface VNetworkChatMessage
{
    public void Serialize(BinaryWriter writer);

    public void Deserialize(BinaryReader reader);
}