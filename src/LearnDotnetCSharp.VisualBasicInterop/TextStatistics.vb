Imports System
Imports System.Collections.Generic
Imports System.Linq

Namespace LearnDotnetCSharp.VisualBasicInterop
    Public NotInheritable Class TextStatistics
        Private Sub New()
        End Sub

        Public Shared Function CountWords(text As String) As IReadOnlyDictionary(Of String, Integer)
            ArgumentNullException.ThrowIfNull(text)

            Dim separators = New Char() {" "c, ChrW(9), ChrW(10), ChrW(13)}
            Return text.Split(separators, StringSplitOptions.RemoveEmptyEntries Or StringSplitOptions.TrimEntries) _
                .GroupBy(Function(word) word, StringComparer.OrdinalIgnoreCase) _
                .ToDictionary(Function(group) group.Key, Function(group) group.Count(), StringComparer.OrdinalIgnoreCase)
        End Function
    End Class
End Namespace
