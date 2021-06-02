﻿using System;
using System.IO;
using OTAPI.Tile;
using Terraria;
using Terraria.ID;
using Terraria.Net.Sockets;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Net;

namespace Crossplay
{
    [ApiVersion(2, 1)]
    public class Crossplay : TerrariaPlugin
    {
        public static int packetHeader = 3;
        public override string Name => "Crossplay for Terraria";
        public override string Author => "Moneylover3246";
        public override string Description => "Enables crossplay for terraria";
        public override Version Version => new Version("1.4.2.3");

        public bool[] IsMobile = new bool[Main.maxPlayers];
        public Crossplay(Main game) : base(game)
        {
            Order = -1;
        }
        public override void Initialize()
        {
            ServerApi.Hooks.NetGetData.Register(this, GetData, 1);
            ServerApi.Hooks.NetSendBytes.Register(this, SendBytes, 10);
            ServerApi.Hooks.NetSendNetData.Register(this, HandleNetModules);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.NetGetData.Deregister(this, GetData);
                ServerApi.Hooks.NetSendBytes.Deregister(this, SendBytes);
                ServerApi.Hooks.NetSendNetData.Deregister(this, HandleNetModules);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
            }
            base.Dispose(disposing);
        }
        // Try to handle data manipulation before tshock + other plugins do so
        private void GetData(GetDataEventArgs args)
        {
            MemoryStream memoryStream = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length);
            using (BinaryReader reader = new BinaryReader(memoryStream))
            {
                try
                {
                    switch (args.MsgID)
                    {
                        case PacketTypes.ConnectRequest:
                            {
                                string version = reader.ReadString();
                                if (version == "Terraria230")
                                {
                                    IsMobile[args.Msg.whoAmI] = true;
                                    byte[] buffer = new PacketFactory().SetType(1).PackString("Terraria" + Main.curRelease).GetByteData();
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine("[Crossplay] Fixing mobile for index " + args.Msg.whoAmI);
                                    Console.ResetColor();
                                    args.Msg.readBuffer.SwapBytes(args.Index - packetHeader, args.Length + (packetHeader - 1), buffer);
                                }
                            }
                            break;
                        case PacketTypes.TileSendSquare:
                            {
                                if (IsMobile[args.Msg.whoAmI])
                                {
                                    ushort size = reader.ReadUInt16();
                                    byte changeType = 0;
                                    if ((size & 32768) > 0)
                                    {
                                        changeType = reader.ReadByte();
                                    }
                                    short tileX = reader.ReadInt16();
                                    short tileY = reader.ReadInt16();

                                    SendTileRectHandler handler = new SendTileRectHandler();
                                    GetDataHandlers.SendTileRectEventArgs STREventArgs = new GetDataHandlers.SendTileRectEventArgs()
                                    {
                                        Player = TShock.Players[args.Msg.whoAmI],
                                        TileX = tileX,
                                        TileY = tileY,
                                        ChangeType = (TileChangeType)changeType,
                                        Data = memoryStream,
                                        Width = (byte)size,
                                        Length = (byte)size,
                                    };
                                    if (SendTileRectHandler.ShouldSkipProcessing(STREventArgs))
                                    {
                                        return;
                                    }
                                    bool[,] processed = new bool[size, size];
                                    NetTile[,] tiles = new NetTile[size, size];
                                    MemoryStream stream = new MemoryStream(args.Msg.readBuffer, (int)(args.Index + reader.BaseStream.Position), args.Length); ;
                                    for (int x = 0; x < size; x++)
                                    {
                                        for (int y = 0; y < size; y++)
                                        {
                                            tiles[x, y] = new NetTile(memoryStream);
                                        }
                                    }
                                    handler.IterateTileRect(tiles, processed, STREventArgs);
                                    args.Handled = true;
                                }
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        // Try to handle data manipulation after other plugins do so
        private void SendBytes(SendBytesEventArgs args)
        {
            int playerIndex = args.Socket.Id;
            try
            {
                if (IsMobile[playerIndex])
                {
                    using (BinaryReader reader = new BinaryReader(new MemoryStream(args.Buffer, 0, args.Buffer.Length)))
                    {
                        int packetlength = reader.ReadInt16();
                        int msgID = reader.ReadByte();
                        switch ((PacketTypes)msgID)
                        {
                            case PacketTypes.ContinueConnecting:
                                {
                                    byte playerID = reader.ReadByte();
                                    bool value = reader.ReadBoolean();
                                    byte[] data = new PacketFactory()
                                        .SetType(3)
                                        .PackByte(playerID)
                                        .GetByteData();
                                    TShock.Players[playerIndex].SendRawData(data);
                                }
                                break;
                            case PacketTypes.WorldInfo:
                                {
                                    byte[] buffer = reader.ReadBytes(22);
                                    string worldName = reader.ReadString();
                                    byte[] buffer2 = reader.ReadBytes(103);
                                    reader.ReadByte(); // Main.tenthAnniversaryWorld
                                    byte[] buffer3 = reader.ReadBytes(27);
                                    byte[] data = new PacketFactory()
                                        .SetType(7)
                                        .PackBuffer(buffer)
                                        .PackString(worldName)
                                        .PackBuffer(buffer2)
                                        .PackBuffer(buffer3)
                                        .GetByteData();
                                    TShock.Players[playerIndex].SendRawData(data);
                                    Array.Clear(args.Buffer, 0, args.Buffer.Length);
                                    args.Handled = true;
                                }
                                break;
                            case PacketTypes.TileSendSquare:
                                {
                                    short tileX = reader.ReadInt16();
                                    short tileY = reader.ReadInt16();
                                    ushort width = reader.ReadByte();
                                    ushort length = reader.ReadByte();
                                    byte tileChangeType = reader.ReadByte();
                                    ushort size = Math.Max(width, length);
                                    PacketFactory data = new PacketFactory()
                                        .SetType(20)
                                        .PackUInt16(size);
                                    if ((size & 0x8000) != 0)
                                    {
                                        data.PackByte(tileChangeType);
                                    }
                                    data.PackInt16(tileX);
                                    data.PackInt16(tileY);
                                    data.PackBuffer(reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position)));
                                    byte[] buffer = data.GetByteData();
                                    TShock.Players[playerIndex].SendRawData(buffer);
                                    args.Handled = true;
                                }
                                break;
                            case PacketTypes.ProjectileNew:
                                {
                                    reader.ReadBytes(19);
                                    short projectileType = reader.ReadInt16();
                                    if (projectileType > 949)
                                    {
                                        Array.Clear(args.Buffer, 0, args.Buffer.Length);
                                        args.Handled = true;
                                    }
                                }
                                break;
                            case PacketTypes.NpcUpdate:
                                {
                                    short npcID = reader.ReadInt16();
                                    NPC npc = Main.npc[npcID];
                                    if (npc.type > 662)
                                    {
                                        Array.Clear(args.Buffer, 0, args.Buffer.Length);
                                        args.Handled = true;
                                    }
                                }
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        /*
         * As of (shortly after) the release of TShock 1.4.2.3, TerrariaServerAPI introduced the NetSendNetData hook,
         * which allows for NetModules to be read on non-proxy platforms, so LoadNetModule can be fixed for TShock.
         */
        private void HandleNetModules(SendNetDataEventArgs args)
        {
            byte[] moduleData = args.packet.Buffer.Data;
            using (BinaryReader reader = new BinaryReader(new MemoryStream(moduleData)))
            {
                reader.ReadInt16(); // Packet Length
                reader.ReadByte(); // Msg Type
                ushort netModuleID = reader.ReadUInt16();
                switch (netModuleID)
                {
                    case 4:
                        byte unlockType = reader.ReadByte();
                        short npcID = reader.ReadInt16();
                        if (npcID > 662)
                        {
                            int index = GetIndexFromSocket(args.socket);
                            if (IsMobile[index])
                            {
                                args.Handled = true;
                                return;
                            }
                        }
                        break;
                    case 5:
                        short itemID = reader.ReadInt16();
                        if (itemID > 5044)
                        {
                            int index = GetIndexFromSocket(args.socket);
                            if (IsMobile[index])
                            {
                                args.Handled = true;
                                return;
                            }
                        }
                        break;
                }
            }
        }
        private void OnLeave(LeaveEventArgs args)
        {
            IsMobile[args.Who] = false;
        }

        private int GetIndexFromSocket(ISocket socket)
        {
            for (int i = 0; i < 255; i++)
            {
                if (Netplay.Clients[i] != null && Netplay.Clients[i].Socket == socket)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
