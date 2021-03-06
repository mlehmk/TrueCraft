﻿using System;
using TrueCraft.API.Server;
using TrueCraft.API.Networking;
using TrueCraft.Core.Networking.Packets;
using TrueCraft.API;
using TrueCraft.Core.Entities;

namespace TrueCraft.Handlers
{
    internal static class LoginHandlers
    {
        public static void HandleHandshakePacket(IPacket packet, IRemoteClient client, IMultiplayerServer server)
        {
            var handshakePacket = (HandshakePacket) packet;
            var remoteClient = (RemoteClient)client;
            remoteClient.Username = handshakePacket.Username;
            remoteClient.QueuePacket(new HandshakeResponsePacket("-")); // TODO: Implement some form of authentication
        }

        public static void HandleLoginRequestPacket(IPacket packet, IRemoteClient client, IMultiplayerServer server)
        {
            var loginRequestPacket = (LoginRequestPacket)packet;
            var remoteClient = (RemoteClient)client;
            if (loginRequestPacket.ProtocolVersion < server.PacketReader.ProtocolVersion)
                remoteClient.QueuePacket(new DisconnectPacket("Client outdated! Use beta 1.7.3."));
            else if (loginRequestPacket.ProtocolVersion > server.PacketReader.ProtocolVersion)
                remoteClient.QueuePacket(new DisconnectPacket("Server outdated! Use beta 1.7.3."));
            else if (server.Worlds.Count == 0)
                remoteClient.QueuePacket(new DisconnectPacket("Server has no worlds configured."));
            else if (!server.PlayerIsWhitelisted(remoteClient.Username) && server.PlayerIsBlacklisted(remoteClient.Username))
                remoteClient.QueuePacket(new DisconnectPacket("You're banned from this server"));
            else
            {
                remoteClient.LoggedIn = true;
                remoteClient.Entity = new PlayerEntity(remoteClient.Username);
                remoteClient.World = server.Worlds[0];
                remoteClient.ChunkRadius = 2;

                if (!remoteClient.Load())
                    remoteClient.Entity.Position = remoteClient.World.SpawnPoint;

                // Send setup packets
                remoteClient.QueuePacket(new LoginResponsePacket(0, 0, Dimension.Overworld));
                remoteClient.UpdateChunks();
                remoteClient.QueuePacket(new WindowItemsPacket(0, remoteClient.Inventory.GetSlots()));
                remoteClient.QueuePacket(new SpawnPositionPacket((int)remoteClient.Entity.Position.X,
                        (int)remoteClient.Entity.Position.Y, (int)remoteClient.Entity.Position.Z));
                remoteClient.QueuePacket(new SetPlayerPositionPacket(remoteClient.Entity.Position.X,
                        remoteClient.Entity.Position.Y + 1,
                        remoteClient.Entity.Position.Y + remoteClient.Entity.Size.Height + 1,
                        remoteClient.Entity.Position.Z, remoteClient.Entity.Yaw, remoteClient.Entity.Pitch, true));
                remoteClient.QueuePacket(new TimeUpdatePacket(remoteClient.World.Time));

                // Start housekeeping for this client
                var entityManager = server.GetEntityManagerForWorld(remoteClient.World);
                entityManager.SpawnEntity(remoteClient.Entity);
                entityManager.SendEntitiesToClient(remoteClient);
                server.Scheduler.ScheduleEvent(DateTime.Now.AddSeconds(10), remoteClient.SendKeepAlive);
                server.Scheduler.ScheduleEvent(DateTime.Now.AddSeconds(1), remoteClient.ExpandChunkRadius);

                if (!string.IsNullOrEmpty(Program.ServerConfiguration.MOTD))
                    remoteClient.SendMessage(Program.ServerConfiguration.MOTD);
                if (!Program.ServerConfiguration.Singleplayer)
                    server.SendMessage(ChatColor.Yellow + "{0} joined the server.", remoteClient.Username);
            }
        }
    }
}