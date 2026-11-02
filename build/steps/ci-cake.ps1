$cakeArguments = @('run', '--project', 'build/Build.csproj', '-c', 'Release', '--no-restore', '--', "--target=$env:CAKE_TARGET", "--output=$env:CAKE_OUTPUT")
if ($env:CAKE_SUITE) { $cakeArguments += "--suite=$env:CAKE_SUITE" }
if ($env:CAKE_FRAMEWORK) { $cakeArguments += "--framework=$env:CAKE_FRAMEWORK" }
& dotnet @cakeArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
