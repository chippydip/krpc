using System;
using System.Security.Cryptography;
using System.Text;
using KRPC.Server;
using KRPC.Server.HTTP;
using KRPC.Utils;

namespace KRPC.Server.WebSockets
{
    static class ConnectionRequest
    {
        const string WEB_SOCKETS_KEY = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        const int BUFFER_SIZE = 4096;
        static readonly SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider ();

        /// <summary>
        /// Read a websockets connection request. If the request is invalid,
        /// writes the approprate HTTP response and denies the connection attempt.
        /// </summary>
        public static HTTPRequest ReadRequest (ClientRequestingConnectionArgs<byte,byte> args)
        {
            try {
                var buffer = new byte [BUFFER_SIZE];
                var count = args.Client.Stream.Read (buffer, 0);
                Logger.WriteLine ("WebSockets connection request:\n" + Encoding.ASCII.GetString (buffer, 0, count), Logger.Severity.Debug);
                var request = HTTPRequest.FromBytes (buffer, 0, count);
                CheckRequest (request);
                return request;
            } catch (HandshakeException e) {
                Logger.WriteLine ("WebSockets handshake exception: " + e.Response);
                args.Request.Deny ();
                args.Client.Stream.Write (e.Response.ToBytes ());
                return null;
            } catch (MalformedHTTPRequestException e) {
                //TODO: wait for timeout seconds to see if the request was truncated
                Logger.WriteLine ("WebSockets malformed request: " + e.Message);
                args.Request.Deny ();
                args.Client.Stream.Write (HTTPResponse.BadRequest.ToBytes ());
                return null;
            }
        }

        public static byte[] WriteResponse (string key)
        {
            var returnKey = Convert.ToBase64String (sha1.ComputeHash (Encoding.ASCII.GetBytes (key + WEB_SOCKETS_KEY)));
            var response = new HTTPResponse (101, "Switching Protocols");
            response.AddHeaderField ("Upgrade", "websocket");
            response.AddHeaderField ("Connection", "Upgrade");
            response.AddHeaderField ("Sec-WebSocket-Accept", returnKey);
            Logger.WriteLine ("WebSockets connection response:\n" + response, Logger.Severity.Debug);
            return response.ToBytes ();
        }

        static void CheckRequest (HTTPRequest request)
        {
            // Check request line
            if (request.Protocol != "HTTP/1.1")
                throw new HandshakeException (HTTPResponse.HTTPVersionNotSupported);
            if (request.URI.AbsolutePath != "/")
                throw new HandshakeException (HTTPResponse.NotFound);
            if (request.Method != "GET")
                throw new HandshakeException (HTTPResponse.MethodNotAllowed, "Expected a GET request.");

            // Check host field
            if (!request.Headers.ContainsKey ("Host"))
                throw new HandshakeException (HTTPResponse.BadRequest, "Host field not set.");

            // Check upgrade field
            if (!request.Headers.ContainsKey ("Upgrade") || request.Headers ["Upgrade"].ToLower () != "websocket")
                throw new HandshakeException (HTTPResponse.BadRequest, "Upgrade field not set to websocket.");

            // Check connection field
            if (!request.Headers.ContainsKey ("Connection") || request.Headers ["Connection"].ToLower () != "upgrade")
                throw new HandshakeException (HTTPResponse.BadRequest, "Connection field not set to Upgrade.");

            // Check key field
            if (!request.Headers.ContainsKey ("Sec-WebSocket-Key"))
                throw new HandshakeException (HTTPResponse.BadRequest, "Sec-WebSocket-Key field not set.");
            try {
                var key = Convert.FromBase64String (request.Headers ["Sec-WebSocket-Key"]);
                if (key.Length != 16)
                    throw new HandshakeException (HTTPResponse.BadRequest, "Failed to decode Sec-WebSocket-Key\nExpected 16 bytes, got " + key.Length + " bytes.");
            } catch (FormatException e) {
                throw new HandshakeException (HTTPResponse.BadRequest, "Failed to decode Sec-WebSocket-Key\n" + e.Message);
            }

            // Check version field
            if (!request.Headers.ContainsKey ("Sec-WebSocket-Version") || request.Headers ["Sec-WebSocket-Version"] != "13") {
                var response = HTTPResponse.UpgradeRequired;
                response.AddHeaderField ("Sec-WebSocket-Version", "13");
                throw new HandshakeException (response);
            }

            // Note: Sec-WebSocket-Protocol and Sec-WebSocket-Extensions fields are ignored
        }
    }
}