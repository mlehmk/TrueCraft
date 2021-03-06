﻿using System;
using TrueCraft.API.Server;
using TrueCraft.API.Networking;
using TrueCraft.Core.Networking;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using TrueCraft.API.World;
using TrueCraft.API.Logging;
using TrueCraft.Core.Networking.Packets;
using TrueCraft.API;
using TrueCraft.Core.Logging;
using TrueCraft.API.Logic;
using TrueCraft.Exceptions;
using TrueCraft.Core.Logic;

namespace TrueCraft
{
    public class MultiplayerServer : IMultiplayerServer
    {
        public event EventHandler<ChatMessageEventArgs> ChatMessageReceived;
        public event EventHandler<PlayerJoinedQuitEventArgs> PlayerJoined;
        public event EventHandler<PlayerJoinedQuitEventArgs> PlayerQuit;

        public IAccessConfiguration AccessConfiguration { get; internal set; }

        public IPacketReader PacketReader { get; private set; }
        public IList<IRemoteClient> Clients { get; private set; }
        public IList<IWorld> Worlds { get; private set; }
        public IList<IEntityManager> EntityManagers { get; private set; }
        public IEventScheduler Scheduler { get; private set; }
        public IBlockRepository BlockRepository { get; private set; }
        public IItemRepository ItemRepository { get; private set; }
        public ICraftingRepository CraftingRepository { get; private set; }
        public bool EnableClientLogging { get; set; }
        public IPEndPoint EndPoint { get; private set; }

        private bool _BlockUpdatesEnabled = true;
        private struct BlockUpdate
        {
            public Coordinates3D Coordinates;
            public IWorld World;
        }
        private Queue<BlockUpdate> PendingBlockUpdates { get; set; }
        public bool BlockUpdatesEnabled
        {
            get
            {
                return _BlockUpdatesEnabled;
            }
            set
            {
                _BlockUpdatesEnabled = value;
                if (_BlockUpdatesEnabled)
                {
                    ProcessBlockUpdates();
                }
            }
        }

        private Timer EnvironmentWorker;
        private Thread NetworkWorker;
        private TcpListener Listener;
        private readonly PacketHandler[] PacketHandlers;
        private IList<ILogProvider> LogProviders;
        internal object ClientLock = new object();
        private bool ShuttingDown = false;

        public MultiplayerServer()
        {
            var reader = new PacketReader();
            PacketReader = reader;
            Clients = new List<IRemoteClient>();
            NetworkWorker = new Thread(new ThreadStart(DoNetwork));
            EnvironmentWorker = new Timer(DoEnvironment);
            PacketHandlers = new PacketHandler[0x100];
            Worlds = new List<IWorld>();
            EntityManagers = new List<IEntityManager>();
            LogProviders = new List<ILogProvider>();
            Scheduler = new EventScheduler(this);
            var blockRepository = new BlockRepository();
            blockRepository.DiscoverBlockProviders();
            BlockRepository = blockRepository;
            var itemRepository = new ItemRepository();
            itemRepository.DiscoverItemProviders();
            ItemRepository = itemRepository;
            var craftingRepository = new CraftingRepository();
            craftingRepository.DiscoverRecipes();
            CraftingRepository = craftingRepository;
            PendingBlockUpdates = new Queue<BlockUpdate>();
            EnableClientLogging = false;

            AccessConfiguration = Configuration.LoadConfiguration<AccessConfiguration>("access.yaml");

            reader.RegisterCorePackets();
            Handlers.PacketHandlers.RegisterHandlers(this);
        }

        public void RegisterPacketHandler(byte packetId, PacketHandler handler)
        {
            PacketHandlers[packetId] = handler;
        }

        public void Start(IPEndPoint endPoint)
        {
            ShuttingDown = false;
            Listener = new TcpListener(endPoint);
            Listener.Start();
            EndPoint = (IPEndPoint)Listener.LocalEndpoint;
            Listener.BeginAcceptTcpClient(AcceptClient, null);
            Log(LogCategory.Notice, "Running TrueCraft server on {0}", EndPoint);
            NetworkWorker.Start();
            EnvironmentWorker.Change(100, 1000 / 20);
        }

        public void Stop()
        {
            ShuttingDown = true;
            Listener.Stop();
            foreach (var w in Worlds)
                w.Save();
            foreach (var c in Clients)
                DisconnectClient(c);
        }

        public void AddWorld(IWorld world)
        {
            Worlds.Add(world);
            world.BlockRepository = BlockRepository;
            world.BlockChanged += HandleBlockChanged;
            var manager = new EntityManager(this, world);
            EntityManagers.Add(manager);
        }

        void HandleBlockChanged(object sender, BlockChangeEventArgs e)
        {
            for (int i = 0, ClientsCount = Clients.Count; i < ClientsCount; i++)
            {
                var client = (RemoteClient)Clients[i];
                // TODO: Confirm that the client knows of this block
                if (client.LoggedIn && client.World == sender)
                {
                    client.QueuePacket(new BlockChangePacket(e.Position.X, (sbyte)e.Position.Y, e.Position.Z,
                            (sbyte)e.NewBlock.ID, (sbyte)e.NewBlock.Metadata));
                }
            }
            PendingBlockUpdates.Enqueue(new BlockUpdate { Coordinates = e.Position, World = sender as IWorld });
            ProcessBlockUpdates();
        }

        private void ProcessBlockUpdates()
        {
            if (!BlockUpdatesEnabled)
                return;
            var adjacent = new[]
            {
                Coordinates3D.Up, Coordinates3D.Down,
                Coordinates3D.Left, Coordinates3D.Right,
                Coordinates3D.Forwards, Coordinates3D.Backwards
            };
            while (PendingBlockUpdates.Count != 0)
            {
                var update = PendingBlockUpdates.Dequeue();
                var source = update.World.GetBlockData(update.Coordinates);
                foreach (var offset in adjacent)
                {
                    var descriptor = update.World.GetBlockData(update.Coordinates + offset);
                    var provider = BlockRepository.GetBlockProvider(descriptor.ID);
                    if (provider != null)
                        provider.BlockUpdate(descriptor, source, this, update.World);
                }
            }
        }

        public void AddLogProvider(ILogProvider provider)
        {
            LogProviders.Add(provider);
        }

        public void Log(LogCategory category, string text, params object[] parameters)
        {
            for (int i = 0, LogProvidersCount = LogProviders.Count; i < LogProvidersCount; i++)
            {
                var provider = LogProviders[i];
                provider.Log(category, text, parameters);
            }
        }

        public IEntityManager GetEntityManagerForWorld(IWorld world)
        {
            for (int i = 0; i < EntityManagers.Count; i++)
            {
                var manager = EntityManagers[i] as EntityManager;
                if (manager.World == world)
                    return manager;
            }
            return null;
        }

        public void SendMessage(string message, params object[] parameters)
        {
            var compiled = string.Format(message, parameters);
            var parts = compiled.Split('\n');
            foreach (var client in Clients)
            {
                foreach (var part in parts)
                    client.SendMessage(part);
            }
            Log(LogCategory.Notice, ChatColor.RemoveColors(compiled));
        }

        protected internal void OnChatMessageReceived(ChatMessageEventArgs e)
        {
            if (ChatMessageReceived != null)
                ChatMessageReceived(this, e);
        }

        protected internal void OnPlayerJoined(PlayerJoinedQuitEventArgs e)
        {
            if (PlayerJoined != null)
                PlayerJoined(this, e);
        }

        protected internal void OnPlayerQuit(PlayerJoinedQuitEventArgs e)
        {
            if (PlayerQuit != null)
                PlayerQuit(this, e);
        }

        private void LogPacket(IPacket packet, bool clientToServer)
        {
            for (int i = 0, LogProvidersCount = LogProviders.Count; i < LogProvidersCount; i++)
            {
                var provider = LogProviders[i];
                packet.Log(provider, clientToServer);
            }
        }

        private void DisconnectClient(IRemoteClient _client)
        {
            var client = (RemoteClient)_client;
            if (client.LoggedIn)
            {
                SendMessage(ChatColor.Yellow + "{0} has left the server.", client.Username);
                GetEntityManagerForWorld(client.World).DespawnEntity(client.Entity);
                GetEntityManagerForWorld(client.World).FlushDespawns();
            }
            client.Save();
            client.Disconnected = true;
            OnPlayerQuit(new PlayerJoinedQuitEventArgs(client));
        }

        private void AcceptClient(IAsyncResult result)
        {
            try
            {
                var tcpClient = Listener.EndAcceptTcpClient(result);
                var client = new RemoteClient(this, tcpClient.GetStream());
                lock (ClientLock)
                    Clients.Add(client);
                Listener.BeginAcceptTcpClient(AcceptClient, null);
            }
            catch
            {
                // Who cares
            }
        }

        private void DoEnvironment(object discarded)
        {
            if (ShuttingDown)
                return;
            Scheduler.Update();
            foreach (var manager in EntityManagers)
            {
                manager.Update();
            }
        }

        private void DoNetwork()
        {
            while (true)
            {
                if (ShuttingDown)
                    return;
                bool idle = true;
                for (int i = 0; i < Clients.Count && i >= 0; i++)
                {
                    RemoteClient client;
                    lock (ClientLock)
                        client = Clients[i] as RemoteClient;

                    if (client == null)
                        continue;

                    while (client.PacketQueue.Count != 0)
                    {
                        idle = false;
                        try
                        {
                            IPacket packet;
                            if (client.PacketQueue.TryTake(out packet))
                            {
                                LogPacket(packet, false);
                                PacketReader.WritePacket(client.MinecraftStream, packet);
                                client.MinecraftStream.BaseStream.Flush();
                                if (packet is DisconnectPacket)
                                {
                                    DisconnectClient(client);
                                    break;
                                }
                            }
                        }
                        catch (SocketException e)
                        {
                            Log(LogCategory.Debug, "Disconnecting client due to exception in network worker");
                            Log(LogCategory.Debug, e.ToString());
                            PacketReader.WritePacket(client.MinecraftStream, new DisconnectPacket("An exception has occured on the server."));
                            client.MinecraftStream.BaseStream.Flush();
                            DisconnectClient(client);
                            break;
                        }
                        catch (Exception e)
                        {
                            Log(LogCategory.Debug, "Disconnecting client due to exception in network worker");
                            Log(LogCategory.Debug, e.ToString());
                            DisconnectClient(client);
                            break;
                        }
                    }
                    if (client.Disconnected)
                    {
                        lock (ClientLock)
                            Clients.RemoveAt(i);
                        break;
                    }
                    const long maxTicks = 100000 * 200; // 200ms
                    var start = DateTime.Now;
                    while (client.DataAvailable && (DateTime.Now.Ticks - start.Ticks) < maxTicks)
                    {
                        idle = false;
                        try
                        {
                            var packet = PacketReader.ReadPacket(client.MinecraftStream);
                            client.LastSuccessfulPacket = packet;
                            LogPacket(packet, true);
                            if (PacketHandlers[packet.ID] != null)
                                PacketHandlers[packet.ID](packet, client, this);
                            else
                                client.Log("Unhandled packet {0}", packet.GetType().Name);
                        }
                        catch (PlayerDisconnectException)
                        {
                            DisconnectClient(client);
                            break;
                        }
                        catch (SocketException e)
                        {
                            Log(LogCategory.Debug, "Disconnecting client due to exception in network worker");
                            Log(LogCategory.Debug, e.ToString());
                            DisconnectClient(client);
                            break;
                        }
                        catch (Exception e)
                        {
                            Log(LogCategory.Debug, "Disconnecting client due to exception in network worker");
                            Log(LogCategory.Debug, e.ToString());
                            try
                            {
                                PacketReader.WritePacket(client.MinecraftStream, new DisconnectPacket("An exception has occured on the server."));
                                client.MinecraftStream.BaseStream.Flush();
                            }
                            catch { /* Silently ignore, by now it's too late */ }
                            DisconnectClient(client);
                            break;
                        }
                    }
                    if (idle)
                        Thread.Sleep(100);
                    if (client.Disconnected)
                    {
                        lock (ClientLock)
                            Clients.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        public bool PlayerIsWhitelisted(string client)
        {
            return AccessConfiguration.Whitelist.Contains(client, StringComparer.CurrentCultureIgnoreCase);
        }

        public bool PlayerIsBlacklisted(string client)
        {
            return AccessConfiguration.Blacklist.Contains(client, StringComparer.CurrentCultureIgnoreCase);
        }

        public bool PlayerIsOp(string client)
        {
            return AccessConfiguration.Oplist.Contains(client, StringComparer.CurrentCultureIgnoreCase);
        }
    }
}
