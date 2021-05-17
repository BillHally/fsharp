module FSharp.Core.UnitTests.Control.Tasks.Coyote

open System
open System.Collections
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Microsoft.FSharp.Control

open Xunit

type IDbCollection =
    abstract CreateRow    : key: string * value: string -> Task<bool>
    abstract GetRow       : key: string                 -> Task<string>
    abstract DeleteRow    : key: string                 -> Task<bool>
    abstract DoesRowExist : key: string                 -> Task<bool>

type AccountManager (accounts: IDbCollection) =
    let createAccount accountName accountPayload =
        task {
            match! accounts.DoesRowExist(accountName) with
            | true -> return false
            | false ->
                // Because tasks "are executed immediately to their first await point",
                // we need to force such a point here, in order to enable
                // the bug to manifest
                do! Task.Delay(0)
                return! accounts.CreateRow(accountName, accountPayload)
        }

    member _.CreateAccount(accountName: string, accountPayload: string) : Task<bool> =
        createAccount accountName accountPayload

    // Returns the accountPayload if the account is found, else null.
    member _.GetAccount(accountName: string) : Task<string> =
        accounts.GetRow(accountName)

    // Returns true if the account is deleted, else false.
    member _.DeleteAccount(accountName: string) : Task<bool> =
        accounts.DeleteRow(accountName)

type RowAlreadyExistsException() = inherit Exception()
type RowNotFoundException()      = inherit Exception()

type InMemoryDbCollection() =
    let collection = ConcurrentDictionary<string, string>()

    let createRow key value =
        task {
            if collection.TryAdd(key, value) then
                return true
            else
                return raise (RowAlreadyExistsException()) // false
        }

    let doesRowExist key = task { return collection.ContainsKey(key) }

    let getRow key =
        task {
            match collection.TryGetValue(key) with
            | true , value -> return value
            | false, _     -> return raise (RowNotFoundException())
        }

    let deleteRow (key : string) =
        task {
            match collection.TryRemove(key) with
            | true , _ -> return true
            | false, _ -> return raise (RowNotFoundException())
        }

    member _.CreateRow(key, value) = createRow key value

    member _.DoesRowExist(key) = doesRowExist key

    member _.GetRow(key) = getRow key

    member _.DeleteRow(key) = deleteRow key

    interface IDbCollection with
        member this.CreateRow   (k, v) = this.CreateRow   (k, v)
        member this.DoesRowExist(k)    = this.DoesRowExist(k)
        member this.GetRow      (k)    = this.GetRow      (k)
        member this.DeleteRow   (k)    = this.DeleteRow   (k)

[<Fact>]
let testAccountCreation () : Task<unit> =
    // Initialize the mock in-memory DB and account manager
    let dbCollection = InMemoryDbCollection()
    let accountManager = AccountManager(dbCollection)

    // Create some dummy data
    let accountName = "MyAccount"
    let accountPayload = "..."

    // Create the account, it should complete successfully and return true
    task {
        let! created = accountManager.CreateAccount(accountName, accountPayload)
        Assert.True(created)

        // Create the same account again. The method should return false this time
        let! recreated = accountManager.CreateAccount(accountName, accountPayload)
        Assert.False(recreated)
    }

// Note the different attribute - this makes it possible for Coyote to run this test from the command line
[<Microsoft.Coyote.SystematicTesting.Test>]
[<Fact>]
let testConcurrentAccountCreation () : Task<unit> =
    task {
        // Initialize the mock in-memory DB and account manager
        let dbCollection   = InMemoryDbCollection()
        let accountManager = AccountManager(dbCollection)

        // Create some dummy data
        let accountName = "MyAccount"
        let accountPayload = "..."

        // Call CreateAccount twice without awaiting, which makes both methods run
        // asynchronously with each other
        let task1 = accountManager.CreateAccount(accountName, accountPayload)
        let task2 = accountManager.CreateAccount(accountName, accountPayload)

        // Then wait both requests to complete
        let! _ = Task.WhenAll(task1, task2)

        // Finally, assert that only one of the two requests succeeded and the other
        // failed. Note that we do not know which one of the two succeeded as the
        // requests ran concurrently (this is why we use an exclusive OR)
        Assert.True(task1.Result <> task2.Result)
    }
