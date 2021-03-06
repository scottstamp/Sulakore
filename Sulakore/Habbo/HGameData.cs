﻿namespace Sulakore.Habbo
{
    public struct HGameData
    {
        private readonly string _host;
        public string Host
        {
            get { return _host; }
        }

        private readonly int _port;
        public int Port
        {
            get { return _port; }
        }

        private readonly int _playerId;
        public int PlayerId
        {
            get { return _playerId; }
        }

        private readonly string _ssoTicket;
        public string SsoTicket
        {
            get { return _ssoTicket; }
        }

        private readonly string _uniqueId;
        public string UniqueId
        {
            get { return _uniqueId; }
        }

        private readonly string _texts;
        public string Texts
        {
            get { return _texts; }
        }

        private readonly string _variables;
        public string Variables
        {
            get { return _variables; }
        }

        private readonly string _clientStarting;
        public string ClientStarting
        {
            get { return _clientStarting; }
        }

        private readonly string _flashClientUrl;
        public string FlashClientUrl
        {
            get { return _flashClientUrl; }
        }

        private readonly string _flashClientBuild;
        public string FlashClientBuild
        {
            get { return _flashClientBuild; }
        }

        private readonly string _figurePartList;
        public string FigurePartList
        {
            get { return _figurePartList; }
        }

        private readonly string _furniDataLoadUrl;
        public string FurniDataLoadUrl
        {
            get { return _furniDataLoadUrl; }
        }

        private readonly string _productDataLoadUrl;
        public string ProductDataLoadUrl
        {
            get { return _productDataLoadUrl; }
        }

        private readonly string _overrideTexts;
        public string OverrideTexts
        {
            get { return _overrideTexts; }
        }

        private readonly string _overrideVariables;
        public string OverrideVariables
        {
            get { return _overrideVariables; }
        }

        public HGameData(string productDataLoadUrl, string overrideVariables, int playerId, string host,
            string variables, string figurePartList, string furniDataLoadUrl, string ssoTicket, string uniqueId,
            string texts, string flashClientUrl, string flashClientBuild, string clientStarting, int port, string overrideTexts)
        {
            _productDataLoadUrl = productDataLoadUrl;
            _overrideVariables = overrideVariables;
            _playerId = playerId;
            _host = host;
            _variables = variables;
            _figurePartList = figurePartList;
            _furniDataLoadUrl = furniDataLoadUrl;
            _ssoTicket = ssoTicket;
            _uniqueId = uniqueId;
            _texts = texts;
            _flashClientUrl = flashClientUrl;
            _flashClientBuild = flashClientBuild;
            _clientStarting = clientStarting;
            _port = port;
            _overrideTexts = overrideTexts;
        }

        public static HGameData Parse(string body)
        {
            body = body.Replace("\\/", "/").Replace("\"//", "\"http://")
                .Replace("'//", "'http://");


            string flashVars = body.GetChild("var flashvars = {", '}')
                .Replace("\"", string.Empty).Replace(" : ", ":");

            string productDataLoadUrl = null, overrideVariables = null, playerId = null, host = null,
                variables = null, figurePartList = null, furniDataLoadUrl = null, ssoTicket = null, uniqueId = null,
                texts = null, flashClientUrl = null, flashClientBuild = null, clientStarting = null, port = null, overrideTexts = null;

            string[] lines = flashVars.Split(',');
            foreach (string pair in lines)
            {
                string varName = pair.Split(':')[0].Trim();
                string varValue = pair.GetChild(varName + ":");

                switch (varName)
                {
                    case "productdata.load.url": productDataLoadUrl = varValue; break;
                    case "external.override.variables.txt": overrideVariables = varValue; break;
                    case "account_id": playerId = varValue; break;
                    case "connection.info.host": host = varValue; break;
                    case "external.variables.txt": variables = varValue; break;
                    case "external.figurepartlist.txt": figurePartList = varValue; break;
                    case "furnidata.load.url": furniDataLoadUrl = varValue; break;
                    case "sso.ticket": ssoTicket = varValue; break;
                    case "unique_habbo_id": uniqueId = varValue; break;
                    case "external.texts.txt": texts = varValue; break;
                    case "flash.client.url":
                    {
                        char valueEnd = '"';
                        string clientUrl = null;
                        int clientUrlIndex = body.IndexOf("embedSWF(");
                        if (clientUrlIndex != -1)
                        {
                            clientUrlIndex += 8;
                            valueEnd = body[clientUrlIndex + 1];
                            bool isVariable = (valueEnd != '"' && valueEnd != '\'');
                            if (isVariable)
                            {
                                clientUrl = body.GetChild(string.Format("{0} = \"",
                                    body.GetChild("embedSWF(", ',')), '"');
                            }
                            else clientUrl = body.GetChild("embedSWF(" + valueEnd, valueEnd);
                            clientUrl = clientUrl.Split('?')[0];
                        }
                        flashClientUrl = clientUrl ?? "http:" + varValue + "Habbo.swf";

                        string[] segments = flashClientUrl.Split('/');
                        flashClientBuild = segments[segments.Length - 2];
                        break;
                    }
                    case "client.starting": clientStarting = varValue; break;
                    case "connection.info.port": port = varValue; break;
                    case "external.override.texts.txt": overrideTexts = varValue; break;
                }
            }

            return new HGameData(productDataLoadUrl, overrideVariables, int.Parse(playerId), host,
                variables, figurePartList, furniDataLoadUrl, ssoTicket, uniqueId, texts,
                flashClientUrl, flashClientBuild, clientStarting, int.Parse(port), overrideTexts);
        }
    }
}