Public Module LibraryFunctions
    ''' Converts fancy mathematical fonts and surrogate-pair unicode glyphs into raw readable ASCII text.
    ''' Crucial for Linux VMs running on Oracle OCI to match strings correctly.
    ''' </summary>
    Public Function DeUnicodeString(input As String) As String
        If String.IsNullOrEmpty(input) Then Return ""

        Dim sb As New System.Text.StringBuilder()
        Dim i As Integer = 0

        While i < input.Length
            ' Check if this character is part of a 4-Byte Surrogate Pair (like mathematical bold text 𝗧𝗼𝗺)
            If Char.IsHighSurrogate(input(i)) AndAlso (i + 1) < input.Length AndAlso Char.IsLowSurrogate(input(i + 1)) Then
                ' Convert the two 16-bit characters into a single 32-bit UTF-32 Code Point
                Dim codePoint As Integer = Char.ConvertToUtf32(input(i), input(i + 1))

                ' 1. Check for Mathematical Bold Capital Letters (𝗧)
                If codePoint >= &H1D5D4 AndAlso codePoint <= &H1D5ED Then
                    sb.Append(Convert.ToChar(codePoint - &H1D5D4 + 65)) ' Map to A-Z
                    ' 2. Check for Mathematical Bold Small Letters (𝗼, 𝗺)
                ElseIf codePoint >= &H1D5EE AndAlso codePoint <= &H1D607 Then
                    sb.Append(Convert.ToChar(codePoint - &H1D5EE + 97)) ' Map to a-z
                Else
                    ' If it's another special character, keep it as is
                    sb.Append(input(i))
                    sb.Append(input(i + 1))
                End If
                i += 2 ' Skip both surrogate characters
            Else
                ' Standard 2-Byte ASCII/Unicode character
                sb.Append(input(i))
                i += 1
            End If
        End While

        Return sb.ToString()
    End Function

End Module
