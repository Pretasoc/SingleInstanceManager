# SingleInstanceManager
A c# wrapper for single instance applications

This project is inspired by [this](https://stackoverflow.com/questions/19147/what-is-the-correct-way-to-create-a-single-instance-application) solution for single instance applications, extended by a mechanism to share the command line parameters of the second instance. This is done using a system wide named pipe.

## Usage

``` csharp
public static int Main(string[] args){
    // submit an optional guid. If no parameter is given the entry assembly name is used.
    var instanceManager = SingleInstanceManager.CreateManager("{GUID}");
        if(instanceManager.RunApplication(args)){
            // Register an event handler for second instances
            instanceManager.SecondInstanceStarted + = OnSecondInstanceStarted;
            // perform other bootstrap operations
            // ...
            // Close the manager, so other instances can start in a valid state       
            instanceManager.Shutdown();
        }
        // perform exit logic
    }
```

