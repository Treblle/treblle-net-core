param($installPath, $toolsPath, $package)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

$project = Get-Project

$fileName = "app.config" 
$file=$project.ProjectItems.Item($fileName)
if ($file -eq $null){
    # Set The Formatting
    $xmlsettings = New-Object System.Xml.XmlWriterSettings
    $xmlsettings.Indent = $true
    $xmlsettings.IndentChars = "    "

    if($project.Properties){
        $projectRoot = $project.Properties.Item("LocalPath")
        if($projectRoot){
            $rootPath = $projectRoot.Value
            $filePath = $rootPath + "\app.config"

            $apiKey = ""
            $projectId = ""

            $TreblleSetupForm                = New-Object system.Windows.Forms.Form
            $TreblleSetupForm.ClientSize     = New-Object System.Drawing.Point(460,167)
            $TreblleSetupForm.text           = "Treblle Setup"
            $TreblleSetupForm.TopMost        = $false
            $TreblleSetupForm.BackColor      = [System.Drawing.ColorTranslator]::FromHtml("#ffffff")

            $Button1                         = New-Object system.Windows.Forms.Button
            $Button1.text                    = "Submit"
            $Button1.width                   = 80
            $Button1.height                  = 30
            $Button1.location                = New-Object System.Drawing.Point(170,74)
            $Button1.Font                    = New-Object System.Drawing.Font('Microsoft Sans Serif',10)
            $Button1.DialogResult            = [System.Windows.Forms.DialogResult]::OK

            $Label1                          = New-Object system.Windows.Forms.Label
            $Label1.text                     = "Treblle API key:"
            $Label1.AutoSize                 = $false
            $Label1.width                    = 155
            $Label1.height                   = 25
            $Label1.location                 = New-Object System.Drawing.Point(14,19)
            $Label1.Font                     = New-Object System.Drawing.Font('Microsoft Sans Serif',10)

            $apiKeyTextBox                   = New-Object system.Windows.Forms.TextBox
            $apiKeyTextBox.multiline         = $false
            $apiKeyTextBox.text              = $apiKey
            $apiKeyTextBox.width             = 232
            $apiKeyTextBox.height            = 20
            $apiKeyTextBox.location          = New-Object System.Drawing.Point(170,15)
            $apiKeyTextBox.Font              = New-Object System.Drawing.Font('Microsoft Sans Serif',10)

            $Label2                          = New-Object system.Windows.Forms.Label
            $Label2.text                     = "Treblle Project ID:"
            $Label2.AutoSize                 = $false
            $Label2.width                    = 155
            $Label2.height                   = 25
            $Label2.location                 = New-Object System.Drawing.Point(14,45)
            $Label2.Font                     = New-Object System.Drawing.Font('Microsoft Sans Serif',10)

            $projectIdTextBox                = New-Object system.Windows.Forms.TextBox
            $projectIdTextBox.multiline      = $false
            $projectIdTextBox.text           = $projectId
            $projectIdTextBox.width          = 232
            $projectIdTextBox.height         = 20
            $projectIdTextBox.location       = New-Object System.Drawing.Point(170,40)
            $projectIdTextBox.Font           = New-Object System.Drawing.Font('Microsoft Sans Serif',10)

            $TreblleSetupForm.controls.AddRange(@($Button1,$Label1,$apiKeyTextBox,$Label2,$projectIdTextBox))

            $result = $TreblleSetupForm.ShowDialog()
    
            if ($result -eq [System.Windows.Forms.DialogResult]::OK)
            {
                $apiKey = $apiKeyTextBox.Text
                $projectId = $projectIdTextBox.Text

                # Set the File Name Create The Document
                $XmlWriter = [System.XML.XmlWriter]::Create($filePath, $xmlsettings)

                # Start the Root Element
                $xmlWriter.WriteStartElement("configuration")
  
                    $xmlWriter.WriteStartElement("appSettings") # <-- Start <Object>
                        $xmlWriter.WriteStartElement("add");
                        $xmlWriter.WriteAttributeString("key", "TreblleApiKey");
                        $xmlWriter.WriteAttributeString("value", $apiKey);
                        $xmlWriter.WriteEndElement();

                        $xmlWriter.WriteStartElement("add");
                        $xmlWriter.WriteAttributeString("key", "TreblleProjectId");
                        $xmlWriter.WriteAttributeString("value", $projectId);
                        $xmlWriter.WriteEndElement();

                    $xmlWriter.WriteEndElement() # <-- End <Object>

                $xmlWriter.WriteEndElement() # <-- End <Root> 

                # End, Finalize and close the XML Document
                $xmlWriter.WriteEndDocument()
                $xmlWriter.Flush()
                $xmlWriter.Close()
            }
            else {

                $apiKey = ""
                $projectId = ""

                # Set the File Name Create The Document
                $XmlWriter = [System.XML.XmlWriter]::Create($filePath, $xmlsettings)

                # Write the XML Decleration and set the XSL
                $xmlWriter.WriteStartDocument()
                $xmlWriter.WriteProcessingInstruction("xml-stylesheet", "type='text/xsl' href='style.xsl'")

                # Start the Root Element
                $xmlWriter.WriteStartElement("configuration")
  
                    $xmlWriter.WriteStartElement("appSettings") # <-- Start <Object>
                        $xmlWriter.WriteStartElement("add");
                        $xmlWriter.WriteAttributeString("key", "TreblleApiKey");
                        $xmlWriter.WriteAttributeString("value", $apiKey);
                        $xmlWriter.WriteEndElement();

                        $xmlWriter.WriteStartElement("add");
                        $xmlWriter.WriteAttributeString("key", "TreblleProjectId");
                        $xmlWriter.WriteAttributeString("value", $projectId);
                        $xmlWriter.WriteEndElement();

                    $xmlWriter.WriteEndElement() # <-- End <Object>

                $xmlWriter.WriteEndElement() # <-- End <Root> 

                # End, Finalize and close the XML Document
                $xmlWriter.WriteEndDocument()
                $xmlWriter.Flush()
                $xmlWriter.Close()
            }
	    }
    }
}
else{
    if($file.Properties){
        # Get localpath
        $localPath = $file.Properties.Item("LocalPath")
        if($localPath){
            $localPath = $localPath.Value   
        }
    }

    if ($localPath -eq $null) {
        Exit
    }
    [XML]$appConfig = Get-Content $localPath

    $apiKeyNode = $appConfig.SelectSingleNode('//configuration/appSettings/add[@key="TreblleApiKey"]')
    $projectIdNode = $appConfig.SelectSingleNode('//configuration/appSettings/add[@key="TreblleProjectId"]')

    if($apiKeyNode -ne $null -and $projectIdNode -ne $null){
        if($apiKeyNode.value -ne $null -and $apiKeyNode.value -ne ""){
            if($projectIdNode.value -ne $null -and $projectIdNode.value -ne ""){
                Exit
		    }
		}
	}
    
    $apiKey = ""
    $projectId = ""

    if($apiKeyNode -ne $null){
        if($apiKeyNode.value -ne $null){
            $apiKey = $apiKeyNode.value
		}
	}
    if($projectIdNode -ne $null){
        if($projectIdNode.value -ne $null){
            $projectId = $projectIdNode.value
		}
	}

    $TreblleSetupForm                = New-Object system.Windows.Forms.Form
    $TreblleSetupForm.ClientSize     = New-Object System.Drawing.Point(460,167)
    $TreblleSetupForm.text           = "Treblle Setup"
    $TreblleSetupForm.TopMost        = $false
    $TreblleSetupForm.BackColor      = [System.Drawing.ColorTranslator]::FromHtml("#ffffff")

    $Button1                         = New-Object system.Windows.Forms.Button
    $Button1.text                    = "Save"
    $Button1.width                   = 80
    $Button1.height                  = 30
    $Button1.location                = New-Object System.Drawing.Point(170,74)
    $Button1.Font                    = New-Object System.Drawing.Font('Microsoft Sans Serif',10)
    $Button1.DialogResult            = [System.Windows.Forms.DialogResult]::OK

    $Label1                          = New-Object system.Windows.Forms.Label
    $Label1.text                     = "Treblle API key:"
    $Label1.AutoSize                 = $false
    $Label1.width                    = 155
    $Label1.height                   = 25
    $Label1.location                 = New-Object System.Drawing.Point(14,19)
    $Label1.Font                     = New-Object System.Drawing.Font('Microsoft Sans Serif',10)

    $apiKeyTextBox                   = New-Object system.Windows.Forms.TextBox
    $apiKeyTextBox.multiline         = $false
    $apiKeyTextBox.text              = $apiKey
    $apiKeyTextBox.width             = 232
    $apiKeyTextBox.height            = 20
    $apiKeyTextBox.location          = New-Object System.Drawing.Point(170,15)
    $apiKeyTextBox.Font              = New-Object System.Drawing.Font('Microsoft Sans Serif',10)

    $Label2                          = New-Object system.Windows.Forms.Label
    $Label2.text                     = "Treblle Project ID:"
    $Label2.AutoSize                 = $false
    $Label2.width                    = 155
    $Label2.height                   = 25
    $Label2.location                 = New-Object System.Drawing.Point(14,45)
    $Label2.Font                     = New-Object System.Drawing.Font('Microsoft Sans Serif',10)

    $projectIdTextBox                = New-Object system.Windows.Forms.TextBox
    $projectIdTextBox.multiline      = $false
    $projectIdTextBox.text           = $projectId
    $projectIdTextBox.width          = 232
    $projectIdTextBox.height         = 20
    $projectIdTextBox.location       = New-Object System.Drawing.Point(170,40)
    $projectIdTextBox.Font           = New-Object System.Drawing.Font('Microsoft Sans Serif',10)

    $TreblleSetupForm.controls.AddRange(@($Button1,$Label1,$apiKeyTextBox,$Label2,$projectIdTextBox))

    $result = $TreblleSetupForm.ShowDialog()
    
    if ($result -eq [System.Windows.Forms.DialogResult]::OK)
    {
        $apiKey = $apiKeyTextBox.Text
        $projectId = $projectIdTextBox.Text

        $appSettings = $appConfig.SelectSingleNode('//configuration/appSettings')
        
        if ($apiKeyNode -ne $null) {
            $apiKeyNode.value = $apiKey
        }
        else{
            $newAddNode = $appConfig.CreateNode("element","add","")
            $newAddNode.SetAttribute("key","TreblleApiKey")
            $newAddNode.SetAttribute("value",$apiKey)
            $appSettings.AppendChild($newAddNode)
        }
    
        if ($projectIdNode -ne $null) {
            $projectIdNode.value = $projectId
        }
        else{
            $newAddNode = $appConfig.CreateNode("element","add","")
            $newAddNode.SetAttribute("key","TreblleProjectId")
            $newAddNode.SetAttribute("value",$projectId)
            $appSettings.AppendChild($newAddNode)
        }

        $appConfig.Save($localPath)
    }
    else {

        $apiKey = ""
        $projectId = ""

        $appSettings = $appConfig.SelectSingleNode('//configuration/appSettings')
        
        if ($apiKeyNode -ne $null) {
            $apiKeyNode.value = $apiKey
        }
        else{
            $newAddNode = $appConfig.CreateNode("element","add","")
            $newAddNode.SetAttribute("key","TreblleApiKey")
            $newAddNode.SetAttribute("value",$apiKey)
            $appSettings.AppendChild($newAddNode)
        }
    
        if ($projectIdNode -ne $null) {
            $projectIdNode.value = $projectId
        }
        else{
            $newAddNode = $appConfig.CreateNode("element","add","")
            $newAddNode.SetAttribute("key","TreblleProjectId")
            $newAddNode.SetAttribute("value",$projectId)
            $appSettings.AppendChild($newAddNode)
        }

        $appConfig.Save($localPath)
    }
}



