# ICM 358210295

https://portal.microsofticm.com/imp/v3/incidents/details/358210295

This project reproduces an issue where the size of an entity grows unbounded as more orchestrations send operations to it.

## Repro Steps

1. Update [this file](https://github.com/cgillum/ICM358210295/blob/master/Properties/launchSettings.json#L7) with the Azure Storage account connection string.
2. Send an HTTP POST request to `http://localhost:7071/tests/StartManyInstances` with the orchestration count as the body (e.g. `100000` for 100K)
3. After the HTTP POST request returns, recycle the function app (kill the process and start it again)
4. Watch memory usage locally and/or the entity state size in Azure Storage
