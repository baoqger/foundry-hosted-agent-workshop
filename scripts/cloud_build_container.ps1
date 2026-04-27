$acrName = (azd env get-values | Select-String "AZURE_CONTAINER_REGISTRY_NAME").ToString().Split("=")[1].Trim('"')
az acr build --registry $acrName --image workshoplab-agent:lab4 --platform linux/amd64 `
    --file ./src/WorkshopLab.AgentHost/Dockerfile `
    ./src