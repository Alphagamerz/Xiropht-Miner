using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xiropht_Connector_All.Seed;

namespace Xiropht_Miner
{
    public class ClassMiningNetwork
    {
        private static ClassSeedNodeConnector classConnector;
        private static bool FirstConnection;
        private static Thread ThreadCheckMiningConnection;
        public static bool IsConnected;
        public static bool IsLogged;
        private static long LastPacketReceived;
        private static bool OnSendingPacket;

        /// <summary>
        /// Start mining.
        /// </summary>
        public static async Task<bool> StartMiningAsync()
        {
            ClassConsole.ConsoleWriteLine("Attempt to connect to pool: " + ClassMiningConfig.MiningPoolHost + ":" + ClassMiningConfig.MiningPoolPort, ClassConsoleEnumeration.IndexPoolConsoleYellowLog);
            if(!await ConnectToMiningPoolAsync())
            {
                ClassConsole.ConsoleWriteLine("Can't connect to pool: " + ClassMiningConfig.MiningPoolHost + ":" + ClassMiningConfig.MiningPoolPort + " retry in 5 seconds.", ClassConsoleEnumeration.IndexPoolConsoleRedLog);
                return false;
            }
            if (IsConnected)
            {
                if (!FirstConnection)
                {
                    FirstConnection = true;
                    ThreadCheckMiningConnection = new Thread(() => CheckMiningPoolConnectionAsync());
                    ThreadCheckMiningConnection.Start();
                }
                await Task.Factory.StartNew(() => ListenMiningPoolAsync(), CancellationToken.None, TaskCreationOptions.RunContinuationsAsynchronously, TaskScheduler.Current).ConfigureAwait(false);
            }
            return true;
        }

        /// <summary>
        /// Permit to connect on a pool.
        /// </summary>
        /// <returns></returns>
        private static async Task<bool> ConnectToMiningPoolAsync()
        {
            if (classConnector == null)
            {
                classConnector = new ClassSeedNodeConnector();
            }
            else
            {
                classConnector?.DisconnectToSeed();
                classConnector?.Dispose();
                classConnector = null;
                classConnector = new ClassSeedNodeConnector();
            }

            try
            {
                if(!await classConnector.StartConnectToSeedAsync(ClassMiningConfig.MiningPoolHost, ClassMiningConfig.MiningPoolPort))
                {
                    IsConnected = false;
                    return false;
                }
                else
                {
                    IsConnected = true;
                    LastPacketReceived = DateTimeOffset.Now.ToUnixTimeSeconds();
                    return true;
                }
            }
            catch
            {
                IsConnected = false;
                return false;
            }
        }

        /// <summary>
        /// Check connection status.
        /// </summary>
        private static async void CheckMiningPoolConnectionAsync()
        {
            while(!Program.Exit)
            {
                try
                {
                    if (classConnector == null)
                    {
                        IsLogged = false;
                        IsConnected = false;
                        OnSendingPacket = false;
                        ClassConsole.ConsoleWriteLine("Miner is disconnected, retry to connect..", ClassConsoleEnumeration.IndexPoolConsoleRedLog);

                        bool status = await StartMiningAsync();
                        while(!status)
                        {
                            await Task.Delay(5000);
                            status = await StartMiningAsync();
                        }
                    }
                    else
                    {
                        if (!classConnector.ReturnStatus() || !IsConnected || LastPacketReceived + 5 <= DateTimeOffset.Now.ToUnixTimeSeconds())
                        {
                            IsLogged = false;
                            IsConnected = false;
                            OnSendingPacket = false;
                            ClassConsole.ConsoleWriteLine("Miner is disconnected, retry to connect..", ClassConsoleEnumeration.IndexPoolConsoleRedLog);
                            bool status = await StartMiningAsync();
                            while (!status)
                            {
                                await Task.Delay(5000);
                                status = await StartMiningAsync();
                            }
                        }
                    }
                }
                catch
                {
                    IsConnected = false;
                }
                Thread.Sleep(1000);
            }
        }


        /// <summary>
        /// Listen mining pool packet to receive.
        /// </summary>
        private static async Task ListenMiningPoolAsync()
        {

            await Task.Factory.StartNew(async delegate ()
            {

                if (!await SendLoginPacketToPoolAsync())
                {
                    IsConnected = false;
                }
                while (IsConnected)
                {
                    try
                    {

                        var packet = await classConnector.ReceivePacketFromSeedNodeAsync(string.Empty, false, false).ConfigureAwait(false);

                        if (packet.Length > 0)
                        {
                            if (packet.Contains(Environment.NewLine))
                            {
                                var splitPacket = packet.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                                if (splitPacket.Length > 1)
                                {
                                    foreach (var packetEach in splitPacket)
                                    {
                                        if (packetEach != null)
                                        {
                                            if (!string.IsNullOrEmpty(packetEach))
                                            {
                                                if (packetEach.Length > 1)
                                                {
                                                    await Task.Factory.StartNew(() => HandleMiningPoolPacket(packetEach), CancellationToken.None, TaskCreationOptions.RunContinuationsAsynchronously, TaskScheduler.Current).ConfigureAwait(false);
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    await Task.Factory.StartNew(() => HandleMiningPoolPacket(packet.Replace(Environment.NewLine, "")), CancellationToken.None, TaskCreationOptions.RunContinuationsAsynchronously, TaskScheduler.Current).ConfigureAwait(false);

                                }
                            }
                        }
                    }
                    catch (Exception error)
                    {
                        Console.WriteLine("Error: " + error.Message);
                        break;
                    }
                }
                IsConnected = false;
            });
        }

        /// <summary>
        /// Disconnect the miner.
        /// </summary>
        public static void DisconnectMiner()
        {
            classConnector?.DisconnectToSeed();
            IsLogged = false;
        }

        /// <summary>
        /// Send login packet to pool
        /// </summary>
        private static async Task<bool> SendLoginPacketToPoolAsync()
        {
            JObject loginPacket = new JObject
            {
                { "type", ClassMiningRequest.TypeLogin },
                { ClassMiningRequest.SubmitWalletAddress, ClassMiningConfig.MiningWalletAddress },
                { ClassMiningRequest.SubmitVersion, Assembly.GetExecutingAssembly().GetName().Version + "R" }
            };

            string loginPacketString = loginPacket.ToString(Formatting.None);
            if (!await SendPacketToPoolAsync(loginPacketString))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Send packet to mining pool
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        public static async Task<bool> SendPacketToPoolAsync(string packet)
        {
            try
            {

                if(!await classConnector.SendPacketToSeedNodeAsync(packet + Environment.NewLine, string.Empty))
                {
                    IsConnected = false;
                    classConnector?.DisconnectToSeed();
                    return false;
                }
            }
            catch
            {
                IsConnected = false;
                classConnector?.DisconnectToSeed();
                return false;
            }

            return true;
        }


        /// <summary>
        /// Handle packets received from pool.
        /// </summary>
        /// <param name="packet"></param>
        private static void HandleMiningPoolPacket(string packet)
        {
            try
            {
                var jsonPacket = JObject.Parse(packet);

                switch (jsonPacket["type"].ToString().ToLower())
                {
                    case ClassMiningRequest.TypeKeepAlive:
                        LastPacketReceived = DateTimeOffset.Now.ToUnixTimeSeconds();
                        break;
                    case ClassMiningRequest.TypeJob:
                        LastPacketReceived = DateTimeOffset.Now.ToUnixTimeSeconds();
                        ClassMining.ListOfCalculation.Clear();
                        IsLogged = true;
                        try
                        {
                            ClassMining.ListOfCalculation.Clear();
                        }
                        catch
                        {

                        }
                        ClassMiningStats.CurrentBlockId = int.Parse(jsonPacket[ClassMiningRequest.TypeBlock].ToString());
                        ClassMiningStats.CurrentBlockTimestampCreate = jsonPacket[ClassMiningRequest.TypeBlockTimestampCreate].ToString();
                        ClassMiningStats.CurrentBlockKey = jsonPacket[ClassMiningRequest.TypeBlockKey].ToString();
                        ClassMiningStats.CurrentBlockIndication = jsonPacket[ClassMiningRequest.TypeBlockIndication].ToString();
                        ClassMiningStats.CurrentMiningJob = float.Parse(jsonPacket[ClassMiningRequest.TypeResult].ToString());
                        ClassMiningStats.CurrentMinRangeJob = float.Parse(jsonPacket[ClassMiningRequest.TypeMinRange].ToString());
                        ClassMiningStats.CurrentMaxRangeJob = float.Parse(jsonPacket[ClassMiningRequest.TypeMaxRange].ToString());
                        ClassMiningStats.CurrentMethodName = jsonPacket[ClassMiningRequest.TypeJobMiningMethodName].ToString();
                        ClassMiningStats.CurrentRoundAesRound = int.Parse(jsonPacket[ClassMiningRequest.TypeJobMiningMethodAesRound].ToString());
                        ClassMiningStats.CurrentRoundAesSize = int.Parse(jsonPacket[ClassMiningRequest.TypeJobMiningMethodAesSize].ToString());
                        ClassMiningStats.CurrentRoundAesKey = jsonPacket[ClassMiningRequest.TypeJobMiningMethodAesKey].ToString();
                        ClassMiningStats.CurrentRoundXorKey = int.Parse(jsonPacket[ClassMiningRequest.TypeJobMiningMethodXorKey].ToString());
                        ClassMining.ProceedMining();

                        ClassConsole.ConsoleWriteLine("New Mining Job: " + ClassMiningStats.CurrentMiningJob + " | Block ID: " + ClassMiningStats.CurrentBlockId + " | Block Difficulty: " + ClassMiningStats.CurrentMaxRangeJob + " | Block Hash Indication: " + ClassMiningStats.CurrentBlockIndication, ClassConsoleEnumeration.IndexPoolConsoleMagentaLog);
                        break;
                    case ClassMiningRequest.TypeShare:
                        LastPacketReceived = DateTimeOffset.Now.ToUnixTimeSeconds();
                        switch (jsonPacket[ClassMiningRequest.TypeResult].ToString().ToLower())
                        {
                            case ClassMiningRequest.TypeResultShareOk:
                                ClassMiningStats.TotalGoodShare++;
                                ClassConsole.ConsoleWriteLine("Good Share ! [Total = " + ClassMiningStats.TotalGoodShare + "]", ClassConsoleEnumeration.IndexPoolConsoleGreenLog);
                                break;
                            case ClassMiningRequest.TypeResultShareInvalid:
                                ClassMiningStats.TotalInvalidShare++;
                                ClassConsole.ConsoleWriteLine("Invalid Share ! [Total = " + ClassMiningStats.TotalInvalidShare + "]", ClassConsoleEnumeration.IndexPoolConsoleRedLog);
                                break;
                            case ClassMiningRequest.TypeResultShareDuplicate:
                                ClassMiningStats.TotalDuplicateShare++;
                                ClassConsole.ConsoleWriteLine("Duplicate Share ! [Total = " + ClassMiningStats.TotalDuplicateShare + "]", ClassConsoleEnumeration.IndexPoolConsoleYellowLog);
                                break;
                            case ClassMiningRequest.TypeResultShareLowDifficulty:
                                ClassMiningStats.TotalLowDifficultyShare++;
                                ClassConsole.ConsoleWriteLine("Low Difficulty Share ! [Total = " + ClassMiningStats.TotalLowDifficultyShare + "]", ClassConsoleEnumeration.IndexPoolConsoleRedLog);
                                break;
                        }
                        break;
                }
            }
            catch
            {

            }
        }
    }
}
