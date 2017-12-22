﻿module FunctionalAsync.Listing

open System
open System.IO
open Microsoft.WindowsAzure
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Blob
open System.Drawing
open FunctionalConcurrency
open System.Drawing.Drawing2D

module Log =
    let Error (ex:Exception) =
        printfn "%s" (ex.Message)

[<AutoOpen>]
module Helpers =
    [<Literal>]
    let azureConnection = "< Azure Connection >"
    let bufferSize = 0x1000

    let getCloudBlobContainerAsync() : Async<CloudBlobContainer> = async {
        let storageAccount = CloudStorageAccount.Parse(azureConnection)
        let blobClient = storageAccount.CreateCloudBlobClient()
        let container = blobClient.GetContainerReference("stuff")
        let! _ = container.CreateIfNotExistsAsync()
        return container }

let log msg = printfn "%s" msg

//Listing 10.5 AsyncOption type-alias in action
let downloadOptionImage(blobReference:string) : AsyncOption<Image> = async {  // #A
    try // #B
        let! container = Helpers.getCloudBlobContainerAsync()
        let blockBlob = container.GetBlockBlobReference(blobReference)
        use memStream = new MemoryStream()
        do! blockBlob.DownloadToStreamAsync(memStream)
        return Some(Bitmap.FromStream(memStream))       // #C
    with                    // #B
    | _ -> return None      // #C
}

let asyncDo =
    downloadOptionImage "Bugghina001.jpg"
    |> Async.map(fun imageOpt ->        // #D
        match imageOpt with             // #E
        | Some(image) -> image.Save("ImageFolder\Bugghina.jpg")
        | None -> log "There was a problem downloading the image")
    |> Async.Start

//Listing 10.6 AsyncOption type-alias in action
let downloadAsyncImage(blobReference:string) : Async<Image> = async {
        let! container = Helpers.getCloudBlobContainerAsync()
        let blockBlob = container.GetBlockBlobReference(blobReference)
        use memStream = new MemoryStream()
        do! blockBlob.DownloadToStreamAsync(memStream)
        return Bitmap.FromStream(memStream)
    }

downloadAsyncImage "Bugghina001.jpg"
|> AsyncOption.handler          // #A
|> Async.map(fun imageOpt ->    // #B
    match imageOpt with         // #C
    | Some(image) -> image.Save("ImageFolder\Bugghina.jpg")
    | None -> log "There was a problem downloading the image")
|> Async.Start

let toThumbnail (image:Image) =
    async {
        let bitmap = image.Clone() :?> Bitmap
        let maxPixels = 400.0
        let scaling =
            if bitmap.Width > bitmap.Height
            then maxPixels / Convert.ToDouble(bitmap.Width)
            else maxPixels / Convert.ToDouble(bitmap.Height)
        let x = Convert.ToInt32(Convert.ToDouble(bitmap.Width) * scaling);
        let y = Convert.ToInt32(Convert.ToDouble(bitmap.Height) * scaling);
        return new Bitmap(bitmap.GetThumbnailImage(x, y, null, IntPtr.Zero)) :> Image;
    } |> AsyncResult.handler

open System.Drawing

let toByteArrayAsync (image:Image) = async {
    use memStream = new MemoryStream()
    do! image.SaveImageAsync(memStream, image.RawFormat)
    return memStream.ToArray() }

let toByteArrayAsyncResult(image : Image) : AsyncResult<byte[]> =
    async {
        use memStream = new MemoryStream()
        image.Save(memStream, image.RawFormat)
        return memStream.ToArray()
    } |> AsyncResult.handler


type logger =
    static member Error (ex:exn) = printfn "Error Message : %s" ex.Message

//Listing 10.14 Leveraging the AsyncResult higher order functions for fluent composition
let processImage (blobReference:string) (destinationImage:string) = //: AsyncResult<unit> =
    async {
        let storageAccount = CloudStorageAccount.Parse("< Azure Connection >")
        let blobClient = storageAccount.CreateCloudBlobClient()
        let container = blobClient.GetContainerReference("Media")
        let! _ = container.CreateIfNotExistsAsync()
        let blockBlob = container.GetBlockBlobReference(blobReference)
        use memStream = new MemoryStream()
        do! blockBlob.DownloadToStreamAsync(memStream)
        return Bitmap.FromStream(memStream) }
    |> AsyncResult.handler  // #A
    |> AsyncResult.bind(fun image -> toThumbnail(image))   // #A
    |> AsyncResult.map(fun image -> toByteArrayAsync(image))    // #A
    |> AsyncResult.bimap (fun bytes -> FileEx.WriteAllBytesAsync(destinationImage, bytes))
                         (fun ex -> logger.Error(ex) |> async.Return)  // #A



//Listing 10.15 Using the AsyncResultBuilder
let processImage2 (blobReference:string) (destinationImage:string) : AsyncResult<unit> =
    asyncResult  {   // #A
        let storageAccount = CloudStorageAccount.Parse("< Azure Connection >")
        let blobClient = storageAccount.CreateCloudBlobClient()
        let container = blobClient.GetContainerReference("Media")
        let! _ = container.CreateIfNotExistsAsync()
        let blockBlob = container.GetBlockBlobReference(blobReference)
        use memStream = new MemoryStream()
        do! blockBlob.DownloadToStreamAsync(memStream)
        let image = Bitmap.FromStream(memStream)
        let! thumbnail = toThumbnail(image)
        return! toByteArrayAsyncResult thumbnail
      }
      |> AsyncResult.bimap (fun bytes -> FileEx.WriteAllBytesAsync(destinationImage, bytes))
                           (fun ex -> logger.Error(ex) |> async.Return)   // #B


module ApplicativeFunctors =

    let downloadOptionImage(blobReference:string) : Async<Image> = async {
        let! container = Helpers.getCloudBlobContainerAsync()
        let blockBlob = container.GetBlockBlobReference(blobReference)
        use memStream = new MemoryStream()
        do! blockBlob.DownloadToStreamAsync(memStream)
        return Bitmap.FromStream(memStream)
    }

    //Listing 10.23 Running in parallel a chain of operations using F# async Applicative Functor
    let blendImages (imageOne:Image) (imageTwo:Image) (size:Size) : Image =
        let bitmap = new Bitmap(size.Width, size.Height)
        use graphic = Graphics.FromImage(bitmap)
        graphic.InterpolationMode <- InterpolationMode.HighQualityBicubic
        graphic.DrawImage(imageOne,
                            new Rectangle(0, 0, size.Width, size.Height),
                            new Rectangle(0, 0, imageOne.Width, imageTwo.Height),
                            GraphicsUnit.Pixel)
        graphic.DrawImage(imageTwo,
                        new Rectangle(0, 0, size.Width, size.Height),
                            new Rectangle(0, 0, imageTwo.Width, imageTwo.Height),
                            GraphicsUnit.Pixel)
        graphic.Save() |> ignore
        bitmap :> Image

    let blendImagesFromBlobStorage (blobReferenceOne:string) (blobReferenceTwo:string) (size:Size) =
        Async.apply(
            Async.apply(
                Async.apply(
                    Async.``pure`` blendImages)
                    (downloadOptionImage(blobReferenceOne)))
                    (downloadOptionImage(blobReferenceTwo)))
                    (Async.``pure`` size)

module Listing_TEST =
    //open System.Drawing.Drawing2D

    let blendImages (imageOne:Image) (imageTwo:Image) (size:Size) : Image =
        let bitmap = new Bitmap(size.Width, size.Height)
        use graphic = Graphics.FromImage(bitmap)
        graphic.InterpolationMode <- InterpolationMode.HighQualityBicubic
        graphic.DrawImage(imageOne,
                            new Rectangle(0, 0, size.Width, size.Height),
                            new Rectangle(0, 0, imageOne.Width, imageTwo.Height),
                            GraphicsUnit.Pixel)
        graphic.DrawImage(imageTwo,
                        new Rectangle(0, 0, size.Width, size.Height),
                            new Rectangle(0, 0, imageTwo.Width, imageTwo.Height),
                            GraphicsUnit.Pixel)
        graphic.Save() |> ignore
        bitmap :> Image


    let downloadOptionImage(blobReference:string) : Async<Image> = async {
            let! container = Helpers.getCloudBlobContainerAsync()
            let blockBlob = container.GetBlockBlobReference(blobReference)
            use memStream = new MemoryStream()
            do! blockBlob.DownloadToStreamAsync(memStream)
            return Bitmap.FromStream(memStream)
        }

    let (<*>) = Async.apply
    let (<!>) = Async.map
    let (<^>) = Async.``pure``

    let blendImagesFromBlobStorage (blobReferenceOne:string) (blobReferenceTwo:string) (size:Size) =
         blendImages
         <!> downloadOptionImage(blobReferenceOne)
         <*> downloadOptionImage(blobReferenceOne)
         <*> Async.``pure`` size


    let blendImagesFromBlobStorage2 (blobReferenceOne:string) (blobReferenceTwo:string) (size:Size) =
        Async.apply(
            Async.apply(
                Async.apply(
                    Async.``pure`` blendImages)
                    (downloadOptionImage(blobReferenceOne)))
                    (downloadOptionImage(blobReferenceOne)))
                    (Async.``pure`` size)

    downloadOptionImage "Bugghina001.jpg"
    |> AsyncOption.handler
    |> Async.map(fun imageOpt ->
        match imageOpt with
        | Some(image) -> image.Save("ImageFolder\Bugghina.jpg")
        | None -> log "There was a problem downloading the image")
    |> Async.Start

module ``Composing and executing heterogeneous parallel computations`` =
    open System
    open System.Net
    open FunctionalConcurrency
    open StockAnalyzer
    open StockAnalysis


    // Listing 10.25  Asynchronous operations to compose and run in parallel
    let calcTransactionAmount amount (price:float) =
        let readyToInvest = amount * 0.75
        let cnt = Math.Floor(readyToInvest / price)
        if (cnt < 1e-5) && (price < amount)
        then 1 else int(cnt)               // #A

    let rnd = Random()
    let mutable bankAccount = 500.0 + float(rnd.Next(1000))
    let getAmountOfMoney() = async {
        return bankAccount
    }    // #B

    let getCurrentPrice symbol = async {
            let! (_,data) = processStockHistory symbol // #H
            return data.[0].open'
    }  // #C

    let getStockIndex index =
        async {
            let url = sprintf "http://download.finance.yahoo.com/d/quotes.csv?s=%s&f=snl1" index
            let req = WebRequest.Create(url)
            let! resp = req.AsyncGetResponse()
            use reader = new StreamReader(resp.GetResponseStream())
            return! reader.ReadToEndAsync()   // #D
        } |> Async.map(fun (row:string) ->
                let items = row.Split(',')
                System.Double.Parse(items.[items.Length-1]))
            |> AsyncResult.handler   // #E

    let analyzeHistoricalTrend symbol =
        asyncResult {
            let! data = getStockHistory symbol (365/2)
            let trend = data.[data.Length-1] - data.[0]
            return trend
        }   // #F

    let withdraw amount = async {
        return
            if amount > bankAccount
            then Error(InvalidOperationException("Not enough money"))
            else
                bankAccount <- bankAccount - amount
                Ok(true)
        }     // #G


    // Listing 10.26  Running heterogeneous asynchronous operations using Applicative Functors
    let howMuchToBuy stockId : AsyncResult<int> =
        Async.lift2 (calcTransactionAmount)   // #A
              (getAmountOfMoney())
              (getCurrentPrice stockId)
        |> AsyncResult.handler         // #B

    let analyze stockId =      // #C
        howMuchToBuy stockId
        |> Async.StartContinuation(function    // #D
            | Ok (total) -> printfn "I recommend to buy %d unit" total
            | Error (e) -> printfn "I do not recommend to buy now")