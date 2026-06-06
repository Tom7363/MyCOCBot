Imports Newtonsoft.Json
Imports System.Collections.Generic

Public Class CocModels

    Public Class FwaRecord
        <JsonProperty("tag")>
        Public Property Tag As String

        <JsonProperty("weight")>
        Public Property Weight As Integer

        <JsonProperty("townhall")>
        Public Property Townhall As Integer

        <JsonProperty("lastModified")>
        Public Property LastModified As String
    End Class

    ' NEU: Hier werden die Klassen global für das gesamte Projekt (inkl. ClanManagerCommands) sichtbar!
    Public Class ClanResponse
        <JsonProperty("memberList")>
        Public Property MemberList As List(Of CocPlayerModel)
    End Class

    Public Class CocPlayerModel
        <JsonProperty("tag")>
        Public Property Tag As String

        <JsonProperty("name")>
        Public Property Name As String

        <JsonProperty("townHallLevel")>
        Public Property TownHallLevel As Integer
    End Class

End Class
