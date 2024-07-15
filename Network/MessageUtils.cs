using System;
using System.Collections.Generic;
using System.IO;
using Bloodstone.API;
using ProjectM;
using ProjectM.Network;

namespace Bloodstone.Network;

public delegate void ClientConnectionMessageHandler(User fromCharacter);

public static class MessageUtils
{
    public static event ClientConnectionMessageHandler? OnClientConnectionEvent;
    
    internal static readonly int ClientNonce = Random.Shared.Next(); 
    private static Dictionary<ulong, int> supportedUsers = new();

    internal struct ClientRegister
    {
        public int clientNonce;
    }
    
    internal static void RegisterClientInitialisationType()
    {
        if (VWorld.IsClient) throw new System.Exception("RegisterClientInitialisationType can only be called on the server.");
        
        VNetworkRegistry.RegisterServerboundStruct((FromCharacter from, ClientRegister register) =>
        {
            var user = VWorld.Server.EntityManager.GetComponentData<User>(from.User);
            supportedUsers[user.PlatformId] = register.clientNonce;
            
            OnClientConnectionEvent?.Invoke(user);
        });
    }
    
    internal static void UnregisterClientInitialisationType()
    {
        if (VWorld.IsClient) throw new System.Exception("UnregisterClientInitialisationType can only be called on the server.");
        
        VNetworkRegistry.UnregisterStruct<ClientRegister>();
    }

    public static void RegisterType<T>(Action<T> onServerMessageEvent) where T : VNetworkChatMessage, new()
    {
        MessageChatRegistry.Register<T>(new()
        {
            OnReceiveFromServer = br =>
            {
                var msg = new T();
                msg.Deserialize(br);
                onServerMessageEvent.Invoke(msg);
            }
        });
    }
    
    public static void InitialiseClient()
    {
        if (VWorld.IsServer) throw new System.Exception("InitialiseClient can only be called on the client.");
        VNetwork.SendToServerStruct(new ClientRegister() { clientNonce = ClientNonce });
    }
    
    public static void SendToClient<T>(User toCharacter, T msg) where T : VNetworkChatMessage
    {
        BloodstonePlugin.Logger.LogDebug("[SERVER] [SEND] VNetworkChatMessage");

        // Note: Bloodstone currently doesn't support sending custom server messages to the client :(
        // VNetwork.SendToClient(toCharacter, msg);
            
        // ... instead we are going to send the user a chat message, as long as we have them in our initialised list.
        if (supportedUsers.TryGetValue(toCharacter.PlatformId, out var userNonce))
        {
            ServerChatUtils.SendSystemMessageToClient(VWorld.Server.EntityManager, toCharacter, $"{SerialiseMessage(msg, userNonce)}");
        }
        else
        {
            BloodstonePlugin.Logger.LogDebug("user nonce not present in supportedUsers");
        }
    }
    
    private static void WriteHeader(BinaryWriter writer, string type, int clientNonce)
    {
        writer.Write(type);
        writer.Write(clientNonce);
    }

    private static bool ReadHeader(BinaryReader reader, out int userNonce, out string type)
    {
        type = "";
        userNonce = 0;
        
        try
        {
            type = reader.ReadString();
            userNonce = reader.ReadInt32();
            
            return true;
        }
        catch (Exception e)
        {
            BloodstonePlugin.Logger.LogDebug($"Failed to read chat message header: {e.Message}");

            return false;
        }
    }

    private static string SerialiseMessage<T>(T msg, int clientNonce) where T : VNetworkChatMessage
    {
        using var stream = new MemoryStream();
        using var bw = new BinaryWriter(stream);
        
        WriteHeader(bw, MessageRegistry.DeriveKey(msg.GetType()), clientNonce);
        
        msg.Serialize(bw);
        return Convert.ToBase64String(stream.ToArray());
    }

    internal static bool DeserialiseMessage(string message)
    {
        var type = "";
        try
        {
            var bytes = Convert.FromBase64String(message);

            using var stream = new MemoryStream(bytes);
            using var br = new BinaryReader(stream);

            // If we can't read the header, it is likely not a VNetworkChatMessage
            if (!ReadHeader(br, out var clientNonce, out type)) return false;

            if (MessageChatRegistry._eventHandlers.TryGetValue(type, out var handler))
            {
                handler.OnReceiveFromServer(br);
            }

            return true;
        }
        catch (FormatException)
        {
            BloodstonePlugin.Logger.LogDebug("Invalid base64");
            return false;
        }
        catch (Exception ex)
        {
            BloodstonePlugin.Logger.LogError($"Error handling incoming network event {type}:");
            BloodstonePlugin.Logger.LogError(ex);
            
            return false;
        }
    }
}