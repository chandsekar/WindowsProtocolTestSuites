﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Protocols.TestTools.StackSdk.Security.KerberosLib.Types;

namespace Microsoft.Protocols.TestTools.StackSdk.Security.KerberosLib
{
    /// <summary>
    /// KDC Proxy Client, wraps KerberosPdus into proxy message, sends them using https and receives the response from server.
    /// </summary>
    public class KKDCPClient
    {
        //response data in bytes
        private byte[] responseBytes;

        //The configuration of this client
        private KKDCPClientConfig config;

        /// <summary>
        /// The realm field of the Kerberos message ([RFC4120] section 5.4.1).
        /// </summary>
        public string TargetDomain;

        /// <summary>
        /// An optional Flags ([MS-NRPC] section 3.5.4.3.1) which contains additional data to be used to find a domain controller for the Kerberos message.
        /// NULL indicates the flag is not present. By default, it is set to null.
        /// </summary>
        public uint? DCLocatorHint;

        /// <summary>
        /// A temporary variable that contains an error message. By default, it is set to STATUS_SUCCESS. 
        /// </summary>
        public KKDCPError Error;

        /// <summary>
        /// Initialize a KKDCPClient.
        /// </summary>
        public KKDCPClient(KKDCPClientConfig config)
        {
            this.config = config;
            //Client do not verify server certificate
            ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
            Error = KKDCPError.STATUS_SUCCESS;
        }

        /// <summary>
        /// Wrap the KerberosPdu provided into proxy message.
        /// </summary>
        /// <param name="pdu">the specified KerberosPdu to be wrapped</param>
        public KDCProxyMessage MakeProxyMessage(KerberosPdu pdu)
        {
            //prepare proxy message
            KDCProxyMessage message = new KDCProxyMessage(pdu);
            if (!string.IsNullOrEmpty(TargetDomain))
            {
                message.TargetDomain = TargetDomain;
            }
            if (DCLocatorHint.HasValue)
            {
                message.DCLocatorHint = DCLocatorHint.Value;
            }
            return message;
        }

        /// <summary>
        /// Send the specified proxy message using https.
        /// </summary>
        /// <param name="message"></param>
        public void SendProxyRequest(KDCProxyMessage message)
        {
            try
            {
                //create web request
                HttpRequestMessage request = new HttpRequestMessage()
                {
                    RequestUri = new Uri(config.KKDCPServerURL),
                    Method = HttpMethod.Post,
                    Version = HttpVersion.Version11,
                    Content = new ByteArrayContent(message.ToBytes())
                };
                request.Headers.Connection.Add("keep-alive");
                request.Headers.UserAgent.ParseAdd("Kerberos/1.0");

                var clientHandler = new HttpClientHandler();
                if (config.TlsClientCertificate != null)
                {
                    clientHandler.ClientCertificates.Add(config.TlsClientCertificate);
                }

                HttpClient client = new HttpClient(clientHandler);

                //send message
                HttpResponseMessage response = client.SendAsync(request).Result;

                //get response
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    //HTTP 403 error received, set ERROR to STATUS_AUTHENTICATION_FIREWALL_FAILED.
                    Error = KKDCPError.STATUS_AUTHENTICATION_FIREWALL_FAILED;
                    return;
                }
                responseBytes = response.Content.ReadAsByteArrayAsync().Result;
            }
            catch (AggregateException ex)
            {
                foreach (var e in ex.Flatten().InnerExceptions)
                {
                    if (e is HttpRequestException)
                    {
                        //server dropped the TCP connection
                        //set Error to STATUS_NO_LOGON_SERVERS
                        Error = KKDCPError.STATUS_NO_LOGON_SERVERS;
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Get the proxy message replied from the server.
        /// NULL will be returned if not response is received.
        /// </summary>
        /// <returns>The responded proxy message</returns>
        public KDCProxyMessage GetProxyResponse()
        {
            if (responseBytes == null)
            {
                return null;
            }
            KDCProxyMessage message = new KDCProxyMessage();
            message.FromBytes(responseBytes);
            return message;
        }
    }
}
