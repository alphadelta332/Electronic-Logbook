VERSION 5.00
Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} frmVerifyCurrency 
   Caption         =   "Verify Flight Reviews, IPCs, and OPCs"
   ClientHeight    =   4920
   ClientLeft      =   120
   ClientTop       =   465
   ClientWidth     =   7965
   OleObjectBlob   =   "frmVerifyCurrency.frx":0000
   StartUpPosition =   1  'CenterOwner
End
Attribute VB_Name = "frmVerifyCurrency"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

Private Sub UserForm_Initialize()
    ConfigureCurrencyVerificationList Me.lstEntries
End Sub

Private Sub cmdExclude_Click()
    If ExcludeSelectedCurrencyEntries(Me.lstEntries) Then Unload Me
End Sub

Private Sub cmdCancel_Click()
    Unload Me
End Sub

