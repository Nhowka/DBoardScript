#if BOOTSTRAP
System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
if not (System.IO.File.Exists "paket.exe") then let url = "https://github.com/fsprojects/Paket/releases/download/3.13.3/paket.exe" in use wc = new System.Net.WebClient() in let tmp = System.IO.Path.GetTempFileName() in wc.DownloadFile(url, tmp); System.IO.File.Move(tmp,System.IO.Path.GetFileName url);;
#r "paket.exe"
Paket.Dependencies.Install (System.IO.File.ReadAllText "paket.dependencies")
#endif

//---------------------------------------------------------------------
#I "packages/WebSharper/lib/net40"
#I "packages/WebSharper.UI.Next/lib/net40"
#I "packages/Microsoft.Owin/lib/net45"
#I "packages/WebSharper.Owin/lib/net45"
#I "packages/WebSharper.Suave/lib/net45"
#I "packages/Suave/lib/net40"
#I "packages/Mono.Cecil/lib/net40"
#I "packages/Owin/lib/net40"
#r "packages/WebSharper.Suave/lib/net45/WebSharper.Suave.dll"
#r "packages/Suave/lib/net40/Suave.dll"
#r "packages/WebSharper.UI.Next/lib/net40/WebSharper.UI.Next.dll"
#r "packages/WebSharper.UI.Next/lib/net40/WebSharper.UI.Next.Templating.dll"
#r "packages/WebSharper/lib/net40/WebSharper.Main.dll"
#r "packages/WebSharper/lib/net40/WebSharper.Web.dll"
#r "packages/WebSharper/lib/net40/WebSharper.Sitelets.dll"
#r "packages/WebSharper/lib/net40/WebSharper.Core.dll"
#r "packages/WebSharper/lib/net40/WebSharper.Javascript.dll"


open System
open Suave                 // always open suave
open Suave.Http
open Suave.Filters
open Suave.Successful // for OK-result
open Suave.Web             // for config
open System.Net
open Suave.Operators 
open WebSharper
open WebSharper.JavaScript
open WebSharper.UI.Next
open WebSharper.UI.Next.Client
open WebSharper.UI.Next.Html
open WebSharper.Sitelets
open WebSharper.UI.Next.Server

module Server =

    [<Rpc>]
    let DoSomething input =
        let R (s: string) = System.String(Array.rev(s.ToCharArray()))
        async {
            return R input
        }

[<JavaScript>]
module Client =

    let Main () =
        let rvInput = Var.Create ""
        let submit = Submitter.CreateOption rvInput.View
        let vReversed =
            submit.View.MapAsync(function
                | None -> async { return "" }
                | Some input -> Server.DoSomething input
            )
        
        div [
            script [text "var ws = new WebSocket(document.URL.replace('http','ws')+'ws'); ws.onmessage=function(msg){CoolBox.innerHTML=msg.data;};"]
            Doc.Input [] rvInput
            Doc.Button "Send" [] submit.Trigger
            hr []
            h4Attr [attr.``class`` "text-muted"] [text "The server responded:"]
            divAttr [attr.``class`` "jumbotron"] [h1Attr [Attr.Create "id" "CoolBox"] [textView vReversed]]
        ]

module SocketCommunication =
    open Suave.WebSocket
    open Suave.Sockets
    open Suave.Sockets.Control
    open Suave.Http
    open System

    let pushServer (webSocket : WebSocket) =
        fun (cx : HttpContext) ->
            socket {
                let loop = ref true
                while !loop do
                    let! msg = webSocket.read()
                    match msg with
                    | (Text, data, true) ->
                        do! webSocket.send Text (Array.concat [ "Websocket: " |> System.Text.Encoding.Default.GetBytes
                                                                cx.connection.socketBinding.ip.GetAddressBytes()
                                                                data ]) true
                    | (Ping, _, _) -> do! webSocket.send Pong ([||]) true
                    | (Close, _, _) ->
                        do! webSocket.send Close ([||]) true
                        loop := false
                    | _ -> ()
            }

type EndPoint =
    | [<EndPoint"/">] Home
    | [<EndPoint"/about">] About

module Templating =

    type MainTemplate = Templating.Template< "Main.html" >

    // Compute a menubar where the menu item for the given endpoint is active
    let MenuBar (ctx : Context<EndPoint>) endpoint : Doc list =
        let (=>) txt act =
            liAttr [ if endpoint = act then yield attr.``class`` "active" ]
                [ aAttr [ attr.href (ctx.Link act) ] [ text txt ] ]
        [ li [ "Home" => EndPoint.Home ]
          li [ "About" => EndPoint.About ] ]

    let Main ctx action title body =
        Content.Page(MainTemplate.Doc(title = title, menubar = MenuBar ctx action, body = body))

module Site =
    open WebSharper.UI.Next.Html

    let HomePage ctx =
        Templating.Main ctx EndPoint.Home "Home" [ h1 [ text "Say Hi to the server!" ]
                                                   div [ client <@ Client.Main() @> ] ]

    let AboutPage ctx =
        Templating.Main ctx EndPoint.About "About"
            [ h1 [ text "About" ]
              p [ text "This is a template WebSharper client-server application." ] ]

    let Main =
        Application.MultiPage(fun ctx endpoint ->
            match endpoint with
            | EndPoint.Home -> HomePage ctx
            | EndPoint.About -> AboutPage ctx)

    open Suave.WebPart
    open WebSharper.Suave
    open Suave.Web
    open Suave.Filters
    open Suave.WebSocket
    open Suave.Operators
    open Suave.Http
    open System.Net
    open Suave.Logging

    let endpoints =
        choose [ path "/ws" >=> handShake SocketCommunication.pushServer
                 (WebSharperAdapter.ToWebPart Main) ]

    let config =
        let port = System.Environment.GetEnvironmentVariable("PORT")
        let ip127 = IPAddress.Parse("127.0.0.1")
        let ipZero = IPAddress.Parse("0.0.0.0")
        { defaultConfig with 
                        logger = Logging.Loggers.saneDefaultsFor Logging.LogLevel.Verbose
                        bindings =
                                 [ (if port = null then HttpBinding.mk HTTP ip127 (uint16 8083)
                                    else HttpBinding.mk HTTP ipZero (uint16 port)) ] }

    do startWebServer config endpoints


