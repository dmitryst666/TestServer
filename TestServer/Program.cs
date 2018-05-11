using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using NLog;
using System.Text.RegularExpressions;

//
// https://habr.com/post/120157/
//


namespace TestServer
{
   
    

    class Server
    {

        public Logger logger = LogManager.GetCurrentClassLogger();
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

                // Принимаем новых клиентов и передаем их на обработку новому экземпляру класса Client -  всё в одном потоке
                /// new Client(Listener.AcceptTcpClient());
                /// 

                //////  ВАРИАНТ 1 - создавать вручную новый поток для каждого клиента
                //// Принимаем нового клиента
                //TcpClient Client = Listener.AcceptTcpClient();
                //// Создаем поток
                //Thread thread = new Thread(new ParameterizedThreadStart(ClientThread));
                //// И запускаем этот поток, передавая ему принятого клиента
                //thread.Name = String.Format("Thread no.: {0}", thr++);
                //thread.Start(Client);

                /// Вариант 2 - воспользоваться пулом потоков.

                // Принимаем новых клиентов. После того, как клиент был принят, он передается в новый поток (ClientThread)
                // с использованием пула потоков.
                ThreadPool.QueueUserWorkItem(new WaitCallback(ClientThread), Listener.AcceptTcpClient());




            }
        }

        // Остановка сервера
        //~Server()
        //{
        //    // Если "слушатель" был создан
        //    if (Listener != null)
        //    {
        //        // Остановим его
                
        //        logger.Trace("TCP listener is stopped.");
                
        //        Listener.Stop();
        //    }
        //}
        

        static void ClientThread(Object StateInfo)
        {
            

            try { 
            new Client((TcpClient)StateInfo);
            } catch(Exception ex)
            {
                Logger logger = LogManager.GetCurrentClassLogger();
                logger.Fatal("Server failed to execute client request, or client side is down.");
            }
        }


    }



    class Program
    {

        static void Main(string[] args)
        {


            // Определим нужное максимальное количество потоков
            // Пусть будет по 4 на каждый процессор
            int MaxThreadsCount = Environment.ProcessorCount * 4;
            // Установим максимальное количество рабочих потоков
            ThreadPool.SetMaxThreads(MaxThreadsCount, MaxThreadsCount);
            // Установим минимальное количество рабочих потоков
            ThreadPool.SetMinThreads(2, 2);

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
            
            logger.Trace("Thread ID: {0}   ================", System.Threading.Thread.CurrentThread.ManagedThreadId);
            // Объявим строку, в которой будет хранится запрос клиента
            string Request = "";
            // Буфер для хранения принятых от клиента данных
            byte[] Buffer = new byte[1024];
            // Переменная для хранения количества байт, принятых от клиента
            int Count;
            // Читаем из потока клиента до тех пор, пока от него поступают данные
            while ((Count = Client.GetStream().Read(Buffer, 0, Buffer.Length)) > 0)
            {
                // Преобразуем эти данные в строку и добавим ее к переменной Request
                Request += Encoding.ASCII.GetString(Buffer, 0, Count);
                // Запрос должен обрываться последовательностью \r\n\r\n
                // Либо обрываем прием данных сами, если длина строки Request превышает 4 килобайта
                // Нам не нужно получать данные из POST-запроса (и т. п.), а обычный запрос
                // по идее не должен быть больше 4 килобайт
                if (Request.IndexOf("\r\n\r\n") >= 0 || Request.Length > 4096)
                {
                    break;
                }
            }
            logger.Trace("Request: {0}", Regex.Replace(Request, "\r\n\r\n",""));


            // Парсим строку запроса с использованием регулярных выражений
            // При этом отсекаем все переменные GET-запроса
            Match ReqMatch = Regex.Match(Request, @"^\w+\s+([^\s\?]+)[^\s]*\s+HTTP/.*|");

            // Если запрос не удался
            if (ReqMatch == Match.Empty)
            {
                // Передаем клиенту ошибку 400 - неверный запрос
                SendError(Client, 400);
                logger.Error("400 - Bad Request");
                return;
            }

            // Получаем строку запроса
            string RequestUri = ReqMatch.Groups[1].Value;

            // Приводим ее к изначальному виду, преобразуя экранированные символы
            // Например, "%20" -> " "
            RequestUri = Uri.UnescapeDataString(RequestUri);

            // Если в строке содержится двоеточие, передадим ошибку 400
            // Это нужно для защиты от URL типа http://example.com/../../file.txt
            if (RequestUri.IndexOf("..") >= 0)
            {
                SendError(Client, 400);
                logger.Error("400 - Bad Request -- Double '..' found!");
                return;
            }

            // Если строка запроса оканчивается на "/", то добавим к ней index.html
            if (RequestUri.EndsWith("/"))
            {
                RequestUri += "index.html";
            }



            string FilePath = "www/" + RequestUri;

            // Если в папке www не существует данного файла, посылаем ошибку 404
            if (!File.Exists(FilePath))
            {
                SendError(Client, 404);
                logger.Error("404 - Not Found");
                return;
            }

            // Получаем расширение файла из строки запроса
            string Extension = RequestUri.Substring(RequestUri.LastIndexOf('.'));

            // Тип содержимого
            string ContentType = "";

            // Пытаемся определить тип содержимого по расширению файла
            switch (Extension)
            {
                case ".htm":
                case ".html":
                    ContentType = "text/html";
                    break;
                case ".css":
                    ContentType = "text/stylesheet";
                    break;
                case ".js":
                    ContentType = "text/javascript";
                    break;
                case ".jpg":
                    ContentType = "image/jpeg";
                    break;
                case ".jpeg":
                case ".png":
                case ".gif":
                    ContentType = "image/" + Extension.Substring(1);
                    break;
                default:
                    if (Extension.Length > 1)
                    {
                        ContentType = "application/" + Extension.Substring(1);
                    }
                    else
                    {
                        ContentType = "application/unknown";
                    }
                    break;
            }


            // Открываем файл, страхуясь на случай ошибки
            FileStream FS;
            try
            {
                FS = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                logger.Trace("Opening File: {0}", FilePath);
                Console.WriteLine("Opening File: {0}", FilePath);
            }
            catch (Exception ex)
            {
                // Если случилась ошибка, посылаем клиенту ошибку 500
                SendError(Client, 500);
                logger.Error("500 - Internal Server Error -- {0}", ex.Message);
                return;
            }

            // Посылаем заголовки
            string Headers = "HTTP/1.1 200 OK\nContent-Type: " + ContentType + "\nContent-Length: " + FS.Length + "\n\n";
            logger.Trace("Headers: {0}", Headers);
            byte[] HeadersBuffer = Encoding.ASCII.GetBytes(Headers);
            Client.GetStream().Write(HeadersBuffer, 0, HeadersBuffer.Length);

            // Пока не достигнут конец файла
            while (FS.Position < FS.Length)
            {
                // Читаем данные из файла
                Count = FS.Read(Buffer, 0, Buffer.Length);
                // И передаем их клиенту
                Client.GetStream().Write(Buffer, 0, Count);
            }

            // Закроем файл и соединение
            logger.Trace("Client thread closed.");
            FS.Close();
            Client.Close();
            

            /////   WORKS
            //    logger.Trace("Request from client");
            //    errCode = "200 OK";
            //    Console.WriteLine("Thread: {0}", System.Threading.Thread.CurrentThread.ManagedThreadId);
            //    StreamReader sr = new StreamReader(Client.GetStream());
            //    string request = sr.ReadLine();
            //   logger.Trace("Request: {0}", request.ToString());

            //    /// Перенаправить запрос на обработку
            //    string Html = controller(request);
            //string Str = "HTTP/1.1 " + errCode + " \nContent-type: text/html\nContent-Length:" + Html.Length.ToString() + "\n\n" + Html;
            //    //// Приведем строку к виду массива байт
            //    byte[] Buffer = Encoding.ASCII.GetBytes(Str);
            //    //// Отправим его клиенту
            //    Client.GetStream().Write(Buffer, 0, Buffer.Length);
            //    //// Закроем соединение
            //    Client.Close();
            ///// WORKS
        }



        // Отправка страницы с ошибкой
        private void SendError(TcpClient Client, int Code)
        {
            // Получаем строку вида "200 OK"
            // HttpStatusCode хранит в себе все статус-коды HTTP/1.1
            string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            // Код простой HTML-странички
            string Html = "<html><body><h1>" + CodeStr + "</h1></body></html>";
            // Необходимые заголовки: ответ сервера, тип и длина содержимого. После двух пустых строк - само содержимое
            string Str = "HTTP/1.1 " + CodeStr + "\nContent-type: text/html\nContent-Length:" + Html.Length.ToString() + "\n\n" + Html;
            // Приведем строку к виду массива байт
            byte[] Buffer = Encoding.ASCII.GetBytes(Str);
            // Отправим его клиенту
            Client.GetStream().Write(Buffer, 0, Buffer.Length);
            // Закроем соединение
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
