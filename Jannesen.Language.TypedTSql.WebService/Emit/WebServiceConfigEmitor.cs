﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Jannesen.Language.TypedTSql.Library;

namespace Jannesen.Language.TypedTSql.WebService.Emit
{
    internal class WebServiceConfigEmitor: FileEmitor
    {
        private abstract class Method
        {
            public                  string                  Path;

            public  abstract        string[]                Assemblies                  { get; }
            public  abstract        string                  HttpHandler                 { get; }

            public  abstract        void                    Emit(Node.WEBSERVICE_EMITOR_WEBSERVICECONFIG configNode, XmlWriter xmlWriter);
        }

        private class WebMethod: Method
        {
            private                 Node.WEBMETHOD          _webMethod;

            public  override        string[]                Assemblies          { get { return _webMethod.n_Declaration.n_WebHandlerAssemblies?.n_Assemblies;  } }
            public  override        string                  HttpHandler         { get { return _webMethod.n_Declaration.n_WebHttpHandler;                      } }

            public                                          WebMethod(Node.WEBMETHOD webMethod)
            {
                this.Path       = webMethod.n_Name;
                this._webMethod = webMethod;
            }

            public  override        void                    Emit(Node.WEBSERVICE_EMITOR_WEBSERVICECONFIG configNode, XmlWriter xmlWriter)
            {
                foreach (var method in _webMethod.n_Declaration.n_Methods) {
                    xmlWriter.WriteStartElement("http-handler");
                        xmlWriter.WriteAttributeString("path",      Path);
                        xmlWriter.WriteAttributeString("verb",      method);
                        xmlWriter.WriteAttributeString("type",      _webMethod.n_Declaration.n_WebHttpHandler);
                        xmlWriter.WriteAttributeString("procedure", _webMethod.EntityName.Fullname);

                        if (_webMethod.n_Declaration.n_WebHandlerOptions != null) {
                            foreach(var optionValue in _webMethod.n_Declaration.n_WebHandlerOptions.n_Options)
                                xmlWriter.WriteAttributeString(optionValue.n_Name, optionValue.n_Value);
                        }

                        if (_webMethod.n_Declaration.n_Attributes?.Attributes != null) {
                            foreach (var attr in _webMethod.n_Declaration.n_Attributes.Attributes) {
                                if (attr.Attr.Name == "description") {
                                    xmlWriter.WriteAttributeString("description", attr.Value.ToString());
                                    break;
                                }
                            }
                        }

                        if (configNode.n_Database != null) { 
                            xmlWriter.WriteAttributeString("database",  configNode.n_Database);
                        }

                        foreach(Node.WEBMETHOD.ServiceParameter arg in _webMethod.n_Parameters.n_Parameters) {
                            var options = arg.n_Options;

                            if (options == null || !options.n_Key || method != "POST") {
                                var source = arg.Source;

                                if (arg.n_Name != null) {
                                    xmlWriter.WriteStartElement("parameter");
                                    xmlWriter.WriteAttributeString("name",      arg.n_Name.Text.Substring(1));
                                    xmlWriter.WriteAttributeString("type",      Node.RETURNS.SqlTypeToString(arg.Parameter.SqlType));
                                    xmlWriter.WriteAttributeString("source",    source);

                                    if (options != null) {
                                        if (!(options.n_Security || options.n_Required))
                                            xmlWriter.WriteAttributeString("optional",  "1");
                                    }
                                    else {
                                        if (source.StartsWith("body:", StringComparison.Ordinal) && method == "DELETE")
                                            xmlWriter.WriteAttributeString("optional",  "1");
                                    }

                                    xmlWriter.WriteEndElement();
                                }
                            }
                        }

                        if (_webMethod.n_returns != null && method != "DELETE") {
                            for (int i = 0 ; i < _webMethod.n_returns.Count ; ++i) {
                                var rtn = _webMethod.n_returns[i];

                                bool dup = false;

                                for (int j = 0 ; j < i ; ++j) {
                                    if (rtn.ResponseTypeName == _webMethod.n_returns[j].ResponseTypeName) {
                                        dup = true;
                                        break;
                                    }
                                }

                                if (!dup) {
                                    xmlWriter.WriteStartElement("response");
                                        xmlWriter.WriteAttributeString("responsemsg", rtn.ResponseTypeName);
                                        Node.RETURNS.WriteResponseXml(xmlWriter, rtn.SqlType);
                                    xmlWriter.WriteEndElement();
                                }
                            }
                        }

                        if (_webMethod.n_Declaration.n_WebHandlerConfig != null)
                            _webMethod.n_Declaration.n_WebHandlerConfig.WriteContentTo(xmlWriter);

                    xmlWriter.WriteEndElement();
                }
            }
        }

        private class IndexMethod: Method
        {
            private                 string                  _procedureName;

            public  override        string[]                Assemblies              { get { return null;                                            } }
            public  override        string                  HttpHandler             { get { return "sql-json2";                                     } }

            public                                          IndexMethod(string path, string procedureName)
            {
                this.Path           = path;
                this._procedureName = procedureName;
            }

            public  override        void                    Emit(Node.WEBSERVICE_EMITOR_WEBSERVICECONFIG configNode, XmlWriter xmlWriter)
            {
                xmlWriter.WriteStartElement("http-handler");
                    xmlWriter.WriteAttributeString("path",      Path);
                    xmlWriter.WriteAttributeString("verb",      "GET");
                    xmlWriter.WriteAttributeString("type",      "sql-json2");
                    xmlWriter.WriteAttributeString("procedure", _procedureName);
                    if (configNode.n_Database != null) { 
                        xmlWriter.WriteAttributeString("database",  configNode.n_Database);
                    }
                    xmlWriter.WriteStartElement("response");
                        xmlWriter.WriteAttributeString("type",        "array:nvarchar(256)");
                    xmlWriter.WriteEndElement();
                xmlWriter.WriteEndElement();
            }
        }

        public      readonly    string                                      Filename;
        public      readonly    Node.WEBSERVICE_EMITOR_WEBSERVICECONFIG     ConfigNode;

        private                 List<Method>                                _methods;
        private                 List<string>                                _loads;

        public                                          WebServiceConfigEmitor(Node.WEBSERVICE_EMITOR_WEBSERVICECONFIG configNode, string basedirectory)
        {
            Filename   = Path.Combine(basedirectory, configNode.n_File);
            ConfigNode = configNode;
            this._methods = new List<Method>();
            this._loads   = new List<string>();
        }

        public                  void                    CleanTarget()
        {
            Library.FileHelpers.DeleteFile(Filename);
        }
        public                  void                    AddWebMethod(Node.WEBMETHOD webMethod)
        {
            _addMethod(new WebMethod(webMethod));
        }
        public                  void                    AddIndexMethod(string pathname, string procedureName)
        {
            _addMethod(new IndexMethod(pathname, procedureName));
        }
        public                  void                    Emit(EmitContext emitContext)
        {
            _methods.Sort((m1, m2) => string.Compare(m1.Path, m2.Path, StringComparison.InvariantCulture));

            try {
                using (var fileData = new MemoryStream()) {
                    using (var xmlWriter = XmlWriter.Create(fileData,
                                                            new XmlWriterSettings() {
                                                                Encoding           = new System.Text.UTF8Encoding(false),
                                                                Indent             = true,
                                                                IndentChars        = "\t",
                                                                CloseOutput        = false
                                                            }))
                    {
                        xmlWriter.WriteStartElement("configuration");

                            foreach(string n in _loads) {
                                xmlWriter.WriteStartElement("load");
                                xmlWriter.WriteAttributeString("name", n);
                                xmlWriter.WriteEndElement();
                            }

                            foreach(var method in _methods)
                                method.Emit(ConfigNode, xmlWriter);

                        xmlWriter.WriteEndElement();
                    }

                    FileUpdate.Update(Filename, fileData);
                }
            }
            catch(Exception err) {
                emitContext.AddEmitError(new EmitError("Emit '" + Filename + "' failed: " + err.Message));
            }
        }

        private                 void                    _addMethod(Method method)
        {
            switch(method.HttpHandler) {
            case "sql-json":
            case "sql-json2":
            case "sql-raw":
            case "sql-xml":
                _adddLoad("Jannesen.Web.MSSql");
                break;
            }

            if (method.Assemblies != null) {
                foreach(string n in method.Assemblies)
                    _adddLoad(n);
            }

            _methods.Add(method);
        }
        private                 void                    _adddLoad(string n)
        {
            if (!_loads.Contains(n))
                _loads.Add(n);
        }
    }
}
