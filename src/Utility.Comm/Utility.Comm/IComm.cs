namespace Utility.Comm
{
  interface IComm
  {
    void Start();

    void Stop();

    bool IsStarted { get; }
    
    bool Send(byte[] message);
  }
}