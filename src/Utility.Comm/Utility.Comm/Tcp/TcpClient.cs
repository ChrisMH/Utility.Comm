using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Utility.Comm.Tcp
{
  public class TcpClient : IComm
  {
    private readonly object sync = new object();

    private Socket socket;
    private bool isStarted;
    private ConnectedSocket connectedSocket;

    private bool stop;

    private const int ReceiveBufferSize = 1024;
    private readonly byte[] receiveBuffer = new byte[ReceiveBufferSize];

    public TcpClient(string sendAddress, int sendPort)
    {
      SendAddress = sendAddress;
      SendPort = sendPort;
    }
      
    public void Start()
    {
      if (IsStarted)
        return;

      lock(sync) stop = false;

      Dns.BeginResolve(SendAddress, ResolveCallback, null);

      IsStarted = true;
    }

    public virtual void Stop()
    {
      if (!IsStarted)
        return;

      lock (sync)
      {
        stop = true;
        socket.Close();
        socket = null;
      }
      IsStarted = false;
    }

    public bool IsStarted
    {
      get
      {
        return isStarted;
      }
      protected set
      {
        if (isStarted == value)
          return;
        isStarted = value;

        var logger = NLog.LogManager.GetCurrentClassLogger();
        if (isStarted)
          logger.Info("{0} Started : {1}:{2}", GetType().Name, SendAddress, SendPort);
        else
          logger.Info("{0} Stopped", GetType().Name);
      }
    }

    public bool Send(byte[] message)
    {
      try
      {
        lock (sync)
        {
          if (socket == null || !socket.Connected)
          {
            return false;
          }
          socket.Send(message);
        }

        return true;
      }
      catch (SocketException ex)
      {
        NLog.LogManager.GetCurrentClassLogger().ErrorException(string.Format("Send : SocketException : {0} : {1}", ex.ErrorCode, ex.Message), ex);

        lock (sync)
        {
          socket.Close();
          socket = null;
        }
        Dns.BeginResolve(SendAddress, ResolveCallback, null);
      }
      catch (Exception ex)
      {
        NLog.LogManager.GetCurrentClassLogger().ErrorException(string.Format("Send : {0} : {1}", ex.GetType(), ex.Message), ex);

        lock (sync)
        {
          socket.Close();
          socket = null;
        }
        Dns.BeginResolve(SendAddress, ResolveCallback, null);
      }
      return false;
    }
    
    private void ResolveCallback(IAsyncResult ar)
    {
      var logger = NLog.LogManager.GetCurrentClassLogger();

      try
      {
        lock(sync) if(stop) return;

        var ipHostEntry = Dns.EndResolve(ar);
        var ipEndPoint = new IPEndPoint(ipHostEntry.AddressList[0], SendPort);

        lock (sync)
        {
          socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
          socket.BeginConnect(ipEndPoint, ConnectCallback, ipEndPoint);
        }

        logger.Info("ResolveCallback : Server address resolved: {0}", ipEndPoint.Address.ToString());
      }
      catch (Exception ex)
      {
        logger.WarnException(string.Format("ResolveCallback : {0} : {1}", ex.GetType(), ex.Message), ex);
        Thread.Sleep(2000);
        lock (sync) if (stop) return;
        Dns.BeginResolve(SendAddress, ResolveCallback, null);
      }
    }

    private void ConnectCallback(IAsyncResult ar)
    {
      var logger = NLog.LogManager.GetCurrentClassLogger();
      try
      {
        lock (sync)
        {
          if(stop) return;
          socket.EndConnect(ar);

          logger.Info("ConnectCallback : Connected");

          SocketError receiveError;
          socket.BeginReceive(receiveBuffer, 0, ReceiveBufferSize, SocketFlags.None, out receiveError, ReceiveCallback, socket);
        }

      }
      catch (SocketException ex)
      {
        logger.WarnException(string.Format("ConnectCallback : SocketException : {0} : {1}", ex.ErrorCode, ex.Message), ex);
        Thread.Sleep(2000);
        lock (sync)
        {
          if(stop) return;
          socket.BeginConnect((IPEndPoint)ar.AsyncState, ConnectCallback, ar.AsyncState);
        }
      }
      catch (Exception ex)
      {
        logger.WarnException(string.Format("ConnectCallback : {0} : {1}", ex.GetType(), ex.Message), ex);
        Thread.Sleep(2000);
        lock (sync)
        {
          if(stop) return;
          socket.BeginConnect((IPEndPoint)ar.AsyncState, ConnectCallback, ar.AsyncState);
        }
      }
    }


    private void ReceiveCallback(IAsyncResult ar)
    {
      var logger = NLog.LogManager.GetCurrentClassLogger();
      try
      {
        int received = 0;
        lock (sync)
        {
          if (stop) return;

          received = socket.EndReceive(ar);
          if (received == 0)
          {
            //if (ConnectedSocketClosed != null) ConnectedSocketClosed(this);
            return;
          }
        }
        
        var buffer = new byte[received];
        Buffer.BlockCopy(receiveBuffer, 0, buffer, 0, received);
        //if (ConnectedSocketReceived != null) ConnectedSocketReceived(this, buffer);

        logger.Info("ReceiveCallback");
        lock(sync)
        {
          if (stop) return;
          SocketError receiveError;
          socket.BeginReceive(receiveBuffer, 0, ReceiveBufferSize, SocketFlags.None, out receiveError, ReceiveCallback, socket);
        }
      }
      catch (SocketException ex)
      {
        lock (sync) if (stop) return;

        if (ex.ErrorCode == 10054)
        {
          // An existing connection was forcibly closed by the remote host
          // Expected when a remote client disconnects.
          return;
        }

        logger.ErrorException(string.Format("ReceiveCallback : SocketException : {0} : {1}", ex.ErrorCode, ex.Message), ex);
      }
      catch (Exception ex)
      {
        lock (sync) if (stop) return;
        logger.ErrorException(string.Format("ReceiveCallback : {0} : {1}", ex.GetType(), ex.Message), ex);
      }
    }

    public string SendAddress { get; private set; }

    public int SendPort { get; private set; }
  }
}