using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.WSA;
using System.Globalization;
using System.Threading;
using UnityEngine.XR.WSA.Sharing;

public class ServerClient {
    public int connectionId;
    public string playerName;
    public GameObject playerPrefab;
    public Vector3 playerPosition;
    public Quaternion playerRotation;
    public bool isLocalPlayer = false;
}

public class PlayBox {
    public int boxId;
    public GameObject prefab;
}

public class NetworkClient : MonoBehaviour {
    [Header("Network Properties")]
    public int port = 3000;
    public float networkMessageSendRate = 0.1f;
    public InputField serverIp;
    public GameObject playerPrefab;
    public GameObject otherPlayerPrefab;

    private string hostIp;
    private int hostConnectionId;
    private float lastSentTime;
    private List<ServerClient> clientsList = new List<ServerClient>();
    private const int MAX_CONNECTIONS = 100;
    private int hostId;
    private int webHostId;
    private int reliableChannel;
    private int unreliableChannel;
    private int connectionId;  
    private float connectionTime;
    private bool isClientConnected = false;
    private bool isServerStarted = false;
    private byte error;

    [Header("Play Boxes Properties")]
    public GameObject boxToPlay;
    public int boxQuantity = 5;
    List<PlayBox> listOfBoxes = new List<PlayBox>();

    [Header("Debug Properties")]
    public Text debugText;
    //public Text boxDebugText;

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private string playerName;
    private char decimalseparator = char.Parse(Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator);

    [Header("Anchor Properties")]
    public GameObject WorldCenter;
    private int retryCount = 10;
    private byte[] importedWorldAnchor;


    public void StartServer()
    {
        if (isServerStarted)
        {
            Debug.Log("Server already started!!!!");
            return;
        }
        if (isClientConnected)
        {
            Debug.Log("Already joined as client!!!");
        }

        ConnectionConfig connectionConfig = new ConnectionConfig();

        reliableChannel = connectionConfig.AddChannel(QosType.Reliable);
        unreliableChannel = connectionConfig.AddChannel(QosType.Unreliable);

        HostTopology networkTopology = new HostTopology(connectionConfig, MAX_CONNECTIONS);

        hostId = NetworkTransport.AddHost(networkTopology, port, null);
       // connectionId = NetworkTransport.AddWebsocketHost(networkTopology, port, null);

        GenerateServerBoxes();

        isServerStarted = true;
        Debug.Log("Hosting Server Locally");
        debugText.text += "Hosting Server Locally | HostConnectionId: " + connectionId + " | ";

        ServerClient host = new ServerClient();
        host.playerName = "Host";
        host.playerPrefab = playerPrefab;
        host.isLocalPlayer = true;
        host.connectionId = connectionId;
        hostConnectionId = connectionId;
        clientsList.Add(host);
    }

    // Start is called before the first frame update
    void Start()
    {
        if (XRDevice.SetTrackingSpaceType(TrackingSpaceType.RoomScale))
        {
            Debug.Log("RoomScale mode was set successfully!!");
        }
        else
        {
            Debug.Log("RoomScale mode was not set successfully");
        }

        WorldCenter.AddComponent<WorldAnchor>();

        NetworkTransport.Init();
    }

    private void GenerateServerBoxes()
    {
        for (int i = 0; i < boxQuantity; i++)
        {
            PlayBox box = new PlayBox();
            box.boxId = i;
            Vector3 position = new Vector3(Random.Range(-2.0f, 2.0f), 0.2f, Random.Range(-2.0f, 2.0f));
            box.prefab = Instantiate(boxToPlay, position, Quaternion.identity);
            listOfBoxes.Add(box);
        }
    }

    public void Connect()
    {
        if (isServerStarted)
        {
            Debug.Log("Cannot be server and client at the same time");
            return;
        }

        if (isClientConnected)
        {
            Debug.Log("Already joined as client!!!");
        }

        playerName = "player_a";

        hostIp = serverIp.text;

        if (serverIp.text == "")
        {
            hostIp = "181.165.152.61";            
        }       

        ConnectionConfig connectionConfig = new ConnectionConfig();

        reliableChannel = connectionConfig.AddChannel(QosType.Reliable);
        unreliableChannel = connectionConfig.AddChannel(QosType.Unreliable);

        HostTopology networkTopology = new HostTopology(connectionConfig, MAX_CONNECTIONS);

        hostId = NetworkTransport.AddHost(networkTopology, 0);
        Debug.Log("connecting to: " + hostIp);
        debugText.text += "connecting to: " + hostIp + " | ";
        connectionId = NetworkTransport.Connect(hostId, hostIp, port, 0, out error);

        Debug.Log((NetworkError)error);

        connectionTime = Time.time;
        isClientConnected = true;
    }

    private void FixedUpdate()
    {
        if (!isClientConnected && !isServerStarted)
        {
            return;
        }

        int recHostId;
        int connectionId;
        int channelId;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error;

        NetworkEventType recData = NetworkTransport.Receive(out recHostId, out connectionId, out channelId, recBuffer, bufferSize, out dataSize, out error);

        switch (recData)
        {
            case NetworkEventType.Nothing:
                break;

            case NetworkEventType.ConnectEvent:
                if (!isServerStarted)
                {
                    Debug.Log("I've connected as client");
                }
                else
                {
                    Debug.Log("Player" + connectionId + " connected");
                    OnClientConnection(connectionId);
                }

                break;

            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                Debug.Log("Recived: " + msg);
                string[] msgArray = msg.Split('|');

                switch (msgArray[0])
                {
                    case "ASKNAME":

                        for (int i = 2; i <= msgArray.Length - 1; i++)
                        {
                            string[] alreadyConnectedPlayer = msgArray[i].Split('%');
                            AddPlayer(int.Parse(alreadyConnectedPlayer[1]), alreadyConnectedPlayer[0]);
                        }
                        SendNetworkMessage("PLYRNAME|" + playerName + "|", reliableChannel, connectionId);
                        lastSentTime = Time.time;
                        break;

                    case "PLYRNAME":

                        Debug.Log("Player" + connectionId + " sent " + msg);
                        SpawnClientPlayer(clientsList.Find(x => x.connectionId == connectionId), msgArray[1]);

                        //Send new player name to other players
                        msg = "ADDPLAYER|" + msgArray[1] + "|" + connectionId;
                        ResendMessageToOtherPlayers(msg, reliableChannel, connectionId);

                        break;

                    case "ADDPLAYER":
                        AddPlayer(int.Parse(msgArray[2]), msgArray[1]);
                        break;

                    case "SPAWNBOX":
                        Vector3 boxPosition = new Vector3(ParseFloatUnit(msgArray[1]), ParseFloatUnit(msgArray[2]), ParseFloatUnit(msgArray[3]));
                        Quaternion boxRotation = new Quaternion(ParseFloatUnit(msgArray[4]), ParseFloatUnit(msgArray[5]), ParseFloatUnit(msgArray[6]), ParseFloatUnit(msgArray[7]));
                        PlayBox box = new PlayBox();
                        box.prefab = Instantiate(boxToPlay, boxPosition, boxRotation);
                        box.prefab.AddComponent<WorldAnchor>();
                        box.boxId = int.Parse(msgArray[8]);
                        box.prefab.transform.hasChanged = false;
                        listOfBoxes.Add(box);
                        break;

                    case "UPDCARTRANS":

                        Vector3 updatedPosition = new Vector3(ParseFloatUnit(msgArray[1]), ParseFloatUnit(msgArray[2]), ParseFloatUnit(msgArray[3]));
                        Quaternion updatedRotation = new Quaternion(ParseFloatUnit(msgArray[4]), ParseFloatUnit(msgArray[5]), ParseFloatUnit(msgArray[6]), ParseFloatUnit(msgArray[7]));
                        if (!isServerStarted)
                        {
                            MoveClientPlayer(clientsList.Find(x => x.connectionId == int.Parse(msgArray[8])), updatedPosition, updatedRotation);
                        }else
                        {
                            MoveClientPlayer(clientsList.Find(x => x.connectionId == connectionId), updatedPosition, updatedRotation);
                            msg = msg + "|" + connectionId;
                            ResendMessageToOtherPlayers(msg,unreliableChannel, connectionId);
                        }

                        break;

                    case "UPDATEBOX":

                        Vector3 updatedBoxPosition = new Vector3(ParseFloatUnit(msgArray[1]), ParseFloatUnit(msgArray[2]), ParseFloatUnit(msgArray[3]));
                        Quaternion updatedBoxRotation = new Quaternion(ParseFloatUnit(msgArray[4]), ParseFloatUnit(msgArray[5]), ParseFloatUnit(msgArray[6]), ParseFloatUnit(msgArray[7]));
                        MoveBox(listOfBoxes.Find(x => x.boxId == int.Parse(msgArray[8])), updatedBoxPosition, updatedBoxRotation);

                        if (isServerStarted)
                        {                            
                            msg = msg + "|" + connectionId;
                            ResendMessageToOtherPlayers(msg, unreliableChannel,connectionId);
                        }
                      
                        break;

                    case "PLAYERDC":
                        Debug.Log("Player" + msgArray[1] + " disconnected");
                        var itemToRemove = clientsList.Single(x => x.connectionId == int.Parse(msgArray[1]));
                        Destroy(itemToRemove.playerPrefab);
                        clientsList.Remove(itemToRemove);
                        break;
                    
                    case "UPDTWANCHOR":
                       // GenerateWorldAnchor(msg.Replace("UPDTWANCHOR|", ""));
                        break;

                    default:
                       // BufferWorldAnchorStream(recBuffer, dataSize);

                        break;
                }



                break;

            case NetworkEventType.DisconnectEvent:
                if (isServerStarted)
                {
                    Debug.Log("Player" + connectionId + " disconnected");
                    var itemToRemove = clientsList.Single(x => x.connectionId == connectionId);
                    Destroy(itemToRemove.playerPrefab);
                    clientsList.Remove(itemToRemove);
                    msg = "PLAYERDC|" + connectionId;
                    ResendMessageToOtherPlayers(msg, unreliableChannel,connectionId);
                }
                break;
        }
        //Debug.Log(Time.time - lastSentTime);
        if ((Time.time - lastSentTime) > networkMessageSendRate)
        {
            if (playerPrefab.transform.hasChanged)
            {            
                UpdateServerCar();
                playerPrefab.transform.hasChanged = false;
                lastSentTime = Time.time;
            }
            foreach (PlayBox box in listOfBoxes)
            {
                if (box.prefab.transform.hasChanged)
                {
                Debug.Log("SENDING!!!");
                    UpdateServerBox(box.boxId, box.prefab.transform.position, box.prefab.transform.rotation);
                    lastSentTime = Time.time;
                    box.prefab.transform.hasChanged = false;
                }
            }
        }
    }

    private void OnClientConnection(int connectionId)
    {
        //Save the player into a list
        AddPlayer(connectionId, "TEMP");        

        //Tell the player his ID and the list of other people and ask the for his name
        string msg = "ASKNAME|" + connectionId + "|";
        foreach (ServerClient c in clientsList)
        {
            if (c.connectionId != connectionId)
            {
                msg += c.playerName + "%" + c.connectionId + "|";
            }
        }
        msg = msg.Trim('|');
        SendNetworkMessage(msg, reliableChannel, connectionId);

        //Tell the player to spawn every box
        foreach (PlayBox box in listOfBoxes)
        {
            msg = "SPAWNBOX|";
            msg += box.prefab.transform.position.x + "|";
            msg += box.prefab.transform.position.y + "|";
            msg += box.prefab.transform.position.z + "|";
            msg += box.prefab.transform.rotation.x + "|";
            msg += box.prefab.transform.rotation.y + "|";
            msg += box.prefab.transform.rotation.z + "|";
            msg += box.prefab.transform.rotation.w + "|";
            msg += box.boxId;
            SendNetworkMessage(msg, reliableChannel, connectionId);
        }

        //Give the player the collection of anchors
        

    }

    private void SendNetworkMessage(string message, int channel, int connectionId)
    {

        if (!isServerStarted)
        {
            if (message.Replace("UPDCARTRANS", "") == message &&
                message.Replace("UPDATEBOX", "") == message)
            {
                Debug.Log("Sending: " + message);
            }

            byte[] msg = Encoding.Unicode.GetBytes(message);
            NetworkTransport.Send(hostId, connectionId, channel, msg, msg.Length, out error);
        }
        else
        {
            List<ServerClient> client = new List<ServerClient>();
            client.Add(clientsList.Find(x => x.connectionId == connectionId));
            SendNetworkMessage(message, channel, client);
        }
    }

    private void SendNetworkMessage(string message, int channel, List<ServerClient> _clientsList)
    {

        byte[] msg = Encoding.Unicode.GetBytes(message);

        foreach (ServerClient client in _clientsList)
        {
            Debug.Log("Sending '" + message);
            Debug.Log("Sending '" + message + "' to " + client.playerName);
            NetworkTransport.Send(hostId, client.connectionId, channel, msg, msg.Length, out error);
        }
    }

    private void SendNetworkMessage(byte[] message, int channel, List<ServerClient> _clientsList)
    {
        foreach (ServerClient client in _clientsList)
        {
            NetworkTransport.Send(hostId, client.connectionId, channel, message, message.Length, out error);
        }
    }

    private void ResendMessageToOtherPlayers(string msg, int channel , int senderConnectionId)
    {
        List<ServerClient> otherClients = new List<ServerClient>();

        foreach (ServerClient client_found in clientsList)
        {
            if (client_found.connectionId != senderConnectionId && client_found.playerName != "Host")
            {
                otherClients.Add(client_found);
            }
        }

        if (otherClients.Count > 0)
        {
            SendNetworkMessage(msg, reliableChannel, otherClients);
        }
    }

    private void ResendMessageToOtherPlayers(string msg, int channel)
    {
        List<ServerClient> otherClients = new List<ServerClient>();

        foreach (ServerClient client_found in clientsList)
        {
            if (client_found.playerName != "Host")
            {
                otherClients.Add(client_found);
            }
        }

        if (otherClients.Count > 0)
        {
            SendNetworkMessage(msg, reliableChannel, otherClients);
        }
    }

    private void AddPlayer(int playersConnectionId, string playersName)
    {
        //Save the player into a list
        ServerClient client = new ServerClient();
        client.connectionId = playersConnectionId;
        client.playerName = playersName;
        if (!isServerStarted)
        {
            client.playerPrefab = Instantiate(otherPlayerPrefab, new Vector3(2, 2, 2), Quaternion.identity);
            client.playerPrefab.AddComponent<WorldAnchor>();
        }
        clientsList.Add(client);
    }

    private float ParseFloatUnit(string value)
    {
        float parsedFloat;
        string parsedValue = value.Replace('.', decimalseparator);
        parsedValue = parsedValue.Replace(',', decimalseparator);

        if (float.TryParse(parsedValue, out parsedFloat))
        {
            return parsedFloat;
        }
        else
        {
            Debug.Log("cannot parse '" + value + "'");
            return 0;
        }

    }

    private void SpawnClientPlayer(ServerClient player, string name)
    {
        player.playerName = name;
        player.playerPrefab = Instantiate(otherPlayerPrefab, new Vector3(0, 0, 0), Quaternion.identity);
    }

    private void MoveClientPlayer(ServerClient player, Vector3 updatedPosition, Quaternion updatedRotation)
    {
        if (player.playerPrefab != null)
        {
            //Debug.Log(updatedPosition);
            DestroyImmediate(player.playerPrefab.GetComponent<WorldAnchor>());
            player.playerPrefab.transform.position = updatedPosition;
            player.playerPrefab.transform.rotation = updatedRotation;
            player.playerPrefab.AddComponent<WorldAnchor>();
        }
    }

    private void MoveBox(PlayBox box, Vector3 updatedPosition, Quaternion updatedRotation)
    {
        if (box.prefab != null)
        {
            DestroyImmediate(box.prefab.GetComponent<WorldAnchor>());
            box.prefab.transform.position = updatedPosition;
            box.prefab.transform.rotation = updatedRotation;
            box.prefab.AddComponent<WorldAnchor>();
            box.prefab.transform.hasChanged = false;
        }
    }

    private void UpdateServerCar()
    {
        string msg = "UPDCARTRANS|";
        msg += playerPrefab.transform.position.x + "|";
        msg += playerPrefab.transform.position.y + "|";
        msg += playerPrefab.transform.position.z + "|";
        msg += playerPrefab.transform.rotation.x + "|";
        msg += playerPrefab.transform.rotation.y + "|";
        msg += playerPrefab.transform.rotation.z + "|";
        msg += playerPrefab.transform.rotation.w;
        if (!isServerStarted)
        {
        SendNetworkMessage(msg, unreliableChannel, connectionId);
        }
        else
        {
            msg += "|" + hostConnectionId;
            ResendMessageToOtherPlayers(msg, unreliableChannel);
        }
    }

    private void UpdateServerBox(int boxId, Vector3 position, Quaternion rotation)
    {
        string msg = "UPDATEBOX|";
        msg += position.x + "|";
        msg += position.y + "|";
        msg += position.z + "|";
        msg += rotation.x + "|";
        msg += rotation.y + "|";
        msg += rotation.z + "|";
        msg += rotation.w + "|";
        msg += boxId;

        if (!isServerStarted)
        {
            SendNetworkMessage(msg, unreliableChannel, connectionId);
        }
        else
        {
            ResendMessageToOtherPlayers(msg, unreliableChannel);
        }
    }

    private void ExportWorldAnchor()
    {
        WorldAnchorTransferBatch transferBatch = new WorldAnchorTransferBatch();
        transferBatch.AddWorldAnchor("GameRootAnchor", WorldCenter.GetComponent<WorldAnchor>());
        WorldAnchorTransferBatch.ExportAsync(transferBatch, OnExportDataAvailable, OnExportComplete);
    }

    private void OnExportComplete(SerializationCompletionReason completionReason)
    {
        if (completionReason != SerializationCompletionReason.Succeeded)
        {
            // If we have been transferring data and it failed, 
            // tell the client to discard the data
            Debug.Log("failed to give anchors, reason: " + completionReason);
            debugText.text += "failed to give anchors, reason: " + completionReason;
        }
        else
        {
            // Tell the client that serialization has succeeded.
            // The client can start importing once all the data is received.
            Debug.Log("success!!");
            debugText.text += "giving anchors succeded";
            string msg = "WANCHOR|END";
            SendNetworkMessage(msg, reliableChannel, connectionId);
        }
    }

    private void OnExportDataAvailable(byte[] data)
    {
        // Send the bytes to the client.  Data may also be buffered.
        NetworkTransport.Send(hostId, connectionId, reliableChannel, data, data.Length, out error);
        //string msg = Encoding.UTF8.GetString(data, 0, data.Length);
        //debugText.text += msg;
        //SendNetworkMessage(msg, reliableChannel,connectionId);
        //Debug.Log(msg);
    }

    private void GenerateWorldAnchor(string importedDataString)
    {
        importedWorldAnchor = Encoding.UTF8.GetBytes(importedDataString);
        ImportWorldAnchor(importedWorldAnchor);
    }

    private void ImportWorldAnchor(byte[] importedData)
    {
        WorldAnchorTransferBatch.ImportAsync(importedData, OnImportComplete);
    }

    private void OnImportComplete(SerializationCompletionReason completionReason, WorldAnchorTransferBatch deserializedTransferBatch)
    {
        if (completionReason != SerializationCompletionReason.Succeeded)
        {
            Debug.Log("Failed to import: " + completionReason.ToString());
            debugText.text += "Failed to import: " + completionReason.ToString();
            if (retryCount > 0)
            {
                retryCount--;
                WorldAnchorTransferBatch.ImportAsync(importedWorldAnchor, OnImportComplete);
            }
            return;
        }

        string[] ids = deserializedTransferBatch.GetAllIds();
        foreach (string id in ids)
        {
            debugText.text += "importing anchor succeded!!";
            deserializedTransferBatch.LockObject(id, WorldCenter);
        }
    }

}
