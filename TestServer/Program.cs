﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NLog;

//
// https://habr.com/post/120157/
//


namespace TestServer
{
   
    

    class Server
    {

        Logger logger = LogManager.GetCurrentClassLogger();
        TcpListener Listener; // Объект, принимающий TCP-клиентов

        // Запуск сервера
        public Server(int Port)
        {
            
            // Создаем "слушателя" для указанного порта
            Listener = new TcpListener(IPAddress.Any, Port);
            logger.Trace("TCP listener at {0}:{1}", IPAddress.Any, Port);
            Listener.Start(); // Запускаем его

            // В бесконечном цикле
            while (true)
            {

                // Принимаем новых клиентов и передаем их на обработку новому экземпляру класса Client
                new Client(Listener.AcceptTcpClient());

            }
        }

        // Остановка сервера
        ~Server()
        {
            // Если "слушатель" был создан
            if (Listener != null)
            {
                // Остановим его
                
                logger.Trace("TCP listener is stopped.");
                
                Listener.Stop();
            }
        }

    }



    class Program
    {

        static void Main(string[] args)
        {
            Logger logger = LogManager.GetCurrentClassLogger();
            logger.Trace("==================================================================\n\n");
            logger.Trace("Server starting....");
            new Server(8080);
        }
    }




    // Класс-обработчик клиента
    class Client
    {
        private string errCode;
        Logger logger = LogManager.GetCurrentClassLogger();
        // Конструктор класса. Ему нужно передавать принятого клиента от TcpListener
        public Client(TcpClient Client)
        {
            
            logger.Trace("Request from client");
            errCode = "200 OK";
            
            //StreamWriter sw = new StreamWriter(client.GetStream());
            //var response = "12 01 40";
            //sw.WriteLine(response);
            StreamReader sr = new StreamReader(Client.GetStream());
            string request = sr.ReadLine();
           logger.Trace("Request: {0}", request.ToString());

            /// Перенаправить запрос на обработку
            /// 
            string Html = controller(request);

            //// Код простой HTML-странички
            //string Html = "<html><body><h1>It works!</h1></body></html>";
            
            //// Необходимые заголовки: ответ сервера, тип и длина содержимого. После двух пустых строк - само содержимое
            //string Str = "HTTP/1.1 200 OK\nContent-type: text/html\nContent-Length:" + Html.Length.ToString() + "\n\n" + Html;
        string Str = "HTTP/1.1 " + errCode + " \nContent-type: text/html\nContent-Length:" + Html.Length.ToString() + "\n\n" + Html;
            //// Приведем строку к виду массива байт
            byte[] Buffer = Encoding.ASCII.GetBytes(Str);
            //// Отправим его клиенту
            Client.GetStream().Write(Buffer, 0, Buffer.Length);
            //// Закроем соединение
            Client.Close();
        }

        public string controller(string req)
        {
            string reply;
            string[] argz = req.Split('/');
            int size = argz.Length;
            int i = 0;
            string splitted = "Splitted: ";
            while (i < size)
            {
                splitted += String.Format("{0}: {1}", i, argz[i]);
                i++;
            }
            logger.Trace(splitted);
            if (argz[0] == "GET "){
                logger.Trace("Request type: {0}", argz[0]);
                            reply = "<html><body><h1>OK!</h1></body></html>";
                            errCode = "200 OK";
                            logger.Trace("Error Code: {0}", errCode);
                logger.Trace("Command: {0}", argz[1]);
                            return reply;


            } else
                {
                reply = "<html><body><h1>Bad Request!</h1><br / > <span style=\"color:red;\">Only GET requests understood for this server!</span></body></html>";
                errCode = "400 	Bad Request";
                logger.Trace("Error Code: {0}", errCode);
                return reply;
                 }

            reply = "<html><body><h1>Not found!</h1></body></html>";
            errCode = "404 Not Found";
            logger.Trace("Error Code: {0}", errCode);
            return reply;
        }
    }

}
